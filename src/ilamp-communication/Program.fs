// Learn more about F# at http://fsharp.org

open System
open bluez.DBus
open Tmds.DBus
open System.IO
open Elmish
open System.Threading
open System.Collections.Generic
open System.Threading.Tasks
open Fable.PowerPack.Keyboard
open System.Buffers
open System.Text
open System.Runtime.InteropServices

type RentMemory (poolMemory, mem) =
    member x.Memory = mem
    member x.Dispose() =
        ArrayPool.Shared.Return(poolMemory, false)
    interface IDisposable with
        member x.Dispose() = x.Dispose()

let parseHex (sin:string) =
    
    // parse 30:31:32:33:34:35:36:37
    let s = sin.Trim(':')
    let withDel = s.Contains (':')
    let byteLength, isOk =
        if not withDel then s.Length / 2, s.Length % 2 = 0
        else 
            ((s.Length - 2) / 3) + 1, s.Length > 2 && (s.Length - 2) % 3 = 0
    if not isOk then
        raise <| ArgumentException(sprintf "can not parse '%s' as hex string" sin)

    let res = ArrayPool.Shared.Rent byteLength
    let multiplier = if withDel then 3 else 2
    for i in 0 .. byteLength-1 do 
        let byteValue = s.Substring(i * multiplier, 2)
        System.Diagnostics.Debug.Assert(not withDel || i = 0 || s.[i * multiplier - 1] = ':', if i = 0 then "i = 0" else sprintf "s.[i * multiplier - 1] = '%c'" s.[i * multiplier - 1])
        res.[i] <- System.Byte.Parse(byteValue, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture)
    new RentMemory(res, new Memory<byte>(res, 0, byteLength))

let toHexString includeDots (b:ReadOnlyMemory<byte>) =
    let baseLength = b.Length * 2
    let sb = new StringBuilder(if includeDots then baseLength + (b.Length - 1) else baseLength)
    let span = b.Span
    for i in 0..b.Length - 1 do
        sb.Append(span.[i].ToString("X2")) |> ignore
        if includeDots && i < b.Length - 1 then sb.Append ':' |> ignore
    sb.ToString()

let tryParseHex (sin:string) =
    try Some (parseHex sin) with _ -> None

module ReflectionBased =
    let rec printEnumerable (d:System.Collections.IEnumerable) : string =
        "[ " + String.Join(";\n  ",
            d |> Seq.cast<obj> |> Seq.map (fun k -> (printValue k).Replace("\n", "\n  "))) + " ]"
    and printKeyValuePair (k, v) =
        let genValue = printValue v
        if genValue.Contains "\n" then
            sprintf "%A ->\n    %s" k (genValue.Replace("\n", "\n    "))
        else sprintf "%A -> %s" k (genValue)
    and printValue (a) : string =
        if isNull a then "<NULL>"
        else
            let t = a.GetType()
            match a with
            | :? System.String as s -> sprintf "%A" s
            | :? System.Collections.IEnumerable as d -> printEnumerable d
            | _ when t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<KeyValuePair<_,_>> ->
                let t = a.GetType()
                let kvpKey = t.GetProperty("Key").GetValue(a, null);
                let kvpValue = t.GetProperty("Value").GetValue(a, null);
                printKeyValuePair (kvpKey, kvpValue)
            | _ -> 
                sprintf "%A" a


type MyAgent () =
    let path =  ObjectPath ("/ilamp/bluez/agent")
    member x.ObjectPath = path
    interface IAgent1 with
        member x.ReleaseAsync() =
            printfn "MyAgent/ReleaseAsync"
            Task.CompletedTask
        member x.RequestPinCodeAsync(device) =
            printfn "RequestPinCodeAsync"
            // The return value should be a string of 1-16 characters
            // length. The string can be alphanumeric.
            Task.FromResult "01234567"
        member x.DisplayPinCodeAsync(device, pinCode) =
            printfn "DisplayPinCodeAsync: %s" pinCode
            Task.CompletedTask
        member x.RequestPasskeyAsync(device) =
            printfn "RequestPasskeyAsync"
            // 0-999999
            Task.FromResult (0u)

        member x.DisplayPasskeyAsync(device, passkey, entered) =
            printfn "DisplayPasskeyAsync %A %A" passkey entered
            Task.CompletedTask
        member x.RequestConfirmationAsync(device, passkey) =
            printfn "RequestConfirmationAsync %A" passkey
            Task.CompletedTask

        member x.RequestAuthorizationAsync(device)=
            printfn "RequestAuthorizationAsync"
            Task.CompletedTask
        member x.AuthorizeServiceAsync(device, uuid) =
            printfn "AuthorizeServiceAsync %A" uuid
            Task.CompletedTask

        member x.CancelAsync() =
            printfn "MyAgent.CancelAsync"
            Task.CompletedTask

    interface IDBusObject with
        member x.ObjectPath = x.ObjectPath

(*
type DemoAgent () =
    interface Agent
    *)

type SelectItemType =
    | Device
    | Adapter
    | Stream
    
type ObjectInfo =
    { Path : ObjectPath
      AdapterProps : Adapter1Properties option
      LEAdvertisingProps : LEAdvertisingManager1Properties option
      DeviceProps : Device1Properties option }
    member x.IsAdapter = x.AdapterProps.IsSome
    member x.IsDevice = x.DeviceProps.IsSome


type ProfileConnection(dispatch : Dispatch<Message>, device:ObjectPath, uuid:string, hnd: SafeHandle, stream:Stream, p:MyProfile) as x =
    let tokenSource = new CancellationTokenSource()
    let handle = hnd
    let startReaderTask (stream:Stream) (tok:CancellationToken) =
        async {
            let lastBeat = System.Diagnostics.Stopwatch.StartNew() 
            try
                // TODO: change logic to first read header and then the rest...
                while not tok.IsCancellationRequested do
                    let rented = ArrayPool.Shared.Rent(106)
                    let mem = new Memory<byte>(rented)
                    //printfn "starting to read.."
                    let handleErr (e : Mono.Unix.UnixIOException) = async {
                        if e.NativeErrorCode = 0 then
                            printfn "ReadAsync -> Success exception"
                            dispatch(ReceivedBytes(x, new RentMemory(rented, mem)))
                            () // Success
                        else
                            match e.ErrorCode with
                            | Mono.Unix.Native.Errno.EWOULDBLOCK ->
                                // data not available yet
                                //use heartbeat = parseHex "01fe0000510210000000008000000080"
                                // to prevent timeout?
                                do! Async.Sleep 500
                                if (lastBeat.ElapsedMilliseconds > 500L) then
                                    dispatch (UserInput "send 01fe0000510210000000008000000080")
                                    lastBeat.Restart()
                            | _ ->                            
                                raise <| exn("Error", e) }
                    try
                        let! read = stream.ReadAsync(mem, tok).AsTask() |> Async.AwaitTask
                        if read > 0 then
                            printfn "read '%d' bytes of data!" read
                            dispatch(ReceivedBytes(x, new RentMemory(rented, mem.Slice(0, read))))
                        else
                            // seems to happen...
                            //printfn "Error: Read stream finished!"
                            ()
                    with
                    | :? System.AggregateException as agg ->
                        match agg.Flatten().InnerException with
                        | :? Mono.Unix.UnixIOException as e -> return! handleErr e
                        | a -> raise <| exn("Error", a)

                    | :? Mono.Unix.UnixIOException as e ->
                        return! handleErr e

                printfn "reading task finished."
                stream.Close()
                stream.Dispose()
            with 
            | e ->
                printfn "reading task crashed: %A" e
        }
        |> Async.StartAsTask

    let readerTask = startReaderTask stream tokenSource.Token

    member x.Device = device
    member x.UUID = uuid
    member x.Profile = p
    member x.StartSend (r:RentMemory) =
        async {
            try
                try
                    do! stream.WriteAsync(Memory.op_Implicit r.Memory).AsTask() |> Async.AwaitTask
                    do! stream.FlushAsync() |> Async.AwaitTask
                    dispatch (ShowOk "Sending succeeded!")
                with e ->
                    dispatch(ShowError (sprintf "Error while sending data: %A" e))
            finally
                r.Dispose()
        }
        |> Async.Start
    member x.Close() =
        tokenSource.Cancel()
        readerTask.Wait()
        handle.Close()

    

and MyProfile (dispatch: Dispatch<Message>, uuid:string) =
    let connections = ResizeArray<ProfileConnection>()
    let path =  ObjectPath ("/ilamp/bluez/myprofile/" + uuid.Replace("-", ""))
    member x.ObjectPath = path
    interface IProfile1 with
        member x.ReleaseAsync() = 
            dispatch (ShowProgress (sprintf "ReleaseAsync ()"))
            Task.CompletedTask
        member x.NewConnectionAsync(device, fd, properties) =
            try
                dispatch (ShowProgress (sprintf "new connection %O => %A" device properties))
                let real = fd.MakeUnclosable()
                let hnd = int (real.DangerousGetHandle())
                let str = new Mono.Unix.UnixStream(hnd, false)
                //let str = new FileStream(fd.DangerousGetHandle())
                let con = new ProfileConnection(dispatch, device, uuid, real, str, x)
                connections.Add(con)
                use init = parseHex "3031323334353637"
                str.Write (Span.op_Implicit init.Memory.Span)
                use heartbeat = parseHex "01fe0000510210000000008000000080"
                str.Write (Span.op_Implicit heartbeat.Memory.Span)
                let z = Array.zeroCreate 16
                let rec doRead (tries) =
                    try
                        let read = str.Read(z, 0 , 16)
                        dispatch (ShowProgress (sprintf "Read %d bytes!!!!!!!!!" read))
                    with 
                    | :? Mono.Unix.UnixIOException as e when tries > 0->
                        if e.ErrorCode = Mono.Unix.Native.Errno.EWOULDBLOCK then
                            Thread.Sleep 50
                            doRead(tries - 1)
                        else reraise()
                doRead(10) 
                dispatch (NewConnectedStream(con))
            with e ->
                dispatch (ShowError (sprintf "Error in NewConnectionAsync: %A" e))
                reraise()
            Task.CompletedTask
        member x.RequestDisconnectionAsync(device) =
            dispatch (ShowProgress (sprintf "connection closed %O" device))
            for d in connections.FindAll(fun con -> con.Device = device) do
                dispatch(DisconnectedStream(d))
                d.Close()
            connections.RemoveAll(fun con -> con.Device = device) |> ignore
            Task.CompletedTask
    interface IDBusObject with
        member x.ObjectPath = x.ObjectPath

    member x.TryFindConnection device =
        let results =
            connections.FindAll(fun con -> con.Device = device)
            |> Seq.toList
        if results.Length > 1 then failwithf "expected only one connection but found %d streams for %O" results.Length device
        results |> List.tryHead

and Message =
    | PrepareList of SelectItemType
    | PrepareDetails of SelectItemType
    | SelectAdapter of ObjectPath
    | SelectDevice of ObjectPath
    | SelectStream of ProfileConnection
    | NewConnectedStream of ProfileConnection
    | DisconnectedStream of ProfileConnection
    | ConnectDevice
    | DisconnectDevice
    | PairDevice
    | UnpairDevice
    | ConnectProfileStream of uuidPrefix:string // selected device
    | UpdateDeviceProperties of ObjectPath * Device1Properties
    | ListAdapterProperties of ObjectPath
    | UserInput of string
    | SendBytes of RentMemory
    | ReceivedBytes of ProfileConnection * RentMemory
    | StartDiscover
    | ObjectConnected of ObjectInfo
    | ObjectDisconnected of ObjectPath
    | ConnectedDBus of IObjectManager * ObjectInfo list
    | ProfileRegistered of string * MyProfile

    // Outputs
    | ShowOk of string
    | ShowProgress of string
    | ShowError of string
    | InvalidUsage of string
    | CouldNotFindItem of SelectItemType * string
    | PrintDetails of SelectItemType
    | PrintPrompt
    | PrintHelp
    | ApplicationExit
    | ConnectionOK

type Model =
    { ObjectManager : IObjectManager option
      AvailableObjects : ObjectInfo[]
      DBusConnectedProfiles : Map<string, MyProfile>
      AvailableStreams : ProfileConnection[]
      SelectedAdapter : ObjectPath option
      SelectedDevice : ObjectPath option
      SelectedStream : ProfileConnection option
      PreviousModel : (Message * Model) option }
    member x.TryFindDevice (path:ObjectPath) =
        x.AvailableObjects |> Seq.tryFind (fun o -> o.IsDevice && o.Path = path)
    member x.TryFindAdapter (path:ObjectPath) =
        x.AvailableObjects |> Seq.tryFind (fun o -> o.IsAdapter && o.Path = path)

    member x.CurrentDevice =
        match x.SelectedDevice with
        | None -> None
        | Some s ->
            x.TryFindDevice s
            |> Option.map (fun i -> i.Path, i.DeviceProps)
            |> Option.orElse (Some (s, None))
    member x.CurrentAdapter =
        match x.SelectedAdapter with
        | None -> None
        | Some s ->
            x.TryFindAdapter s
            |> Option.map (fun i -> i.Path, i.AdapterProps)
            |> Option.orElse (Some (s, None))

let applicationExit = new CancellationTokenSource()


let isServer, dbusBus = true, new Connection(Address.System)
// let isServer, dbusBus = false, Connection.System
let bluezName = "org.bluez"

let readInputs (dispatch:Dispatch<Message>) =
    async {
        try
            while true do
                let input = Console.ReadLine()
                dispatch (UserInput input)
        with e ->
            printfn "INPUT LOOP FAILED: %A" e
    }
    |> Async.Start
    |> ignore


let trySet (values:IDictionary<_,_>) key f =
    match values.TryGetValue key with
    | true, v -> f v
    | _ -> ()

let getDevice key (kv:IDictionary<_,IDictionary<_,obj>>) =
    let adapter =
        match kv.TryGetValue "org.bluez.Adapter1" with
        | true, values ->
            let adapterInfo = Adapter1Properties()
            trySet values "Address" (fun e -> adapterInfo.Address <- e :?> _)
            trySet values "AddressType" (fun e -> adapterInfo.AddressType <- e :?> _)
            trySet values "Name" (fun e -> adapterInfo.Name <- e :?> _)
            trySet values "Alias" (fun e -> adapterInfo.Alias <- e :?> _)
            trySet values "Class" (fun e -> adapterInfo.Class <- e :?> _)
            trySet values "Powered" (fun e -> adapterInfo.Powered <- e :?> _)
            trySet values "Discoverable" (fun e -> adapterInfo.Discoverable <- e :?> _)
            trySet values "DiscoverableTimeout" (fun e -> adapterInfo.DiscoverableTimeout <- e :?> _)
            trySet values "Pairable" (fun e -> adapterInfo.Pairable <- e :?> _)
            trySet values "PairableTimeout" (fun e -> adapterInfo.PairableTimeout <- e :?> _)
            trySet values "UUIDs" (fun e -> adapterInfo.UUIDs <- e :?> _)
            trySet values "Modalias" (fun e -> adapterInfo.Modalias <- e :?> _)
            Some adapterInfo
        | _ -> None
    let leadvertising =
        match kv.TryGetValue "org.bluez.LEAdvertisingManager1" with
        | true, values ->
            let advertisingProps = LEAdvertisingManager1Properties()
            trySet values "ActiveInstances" (fun e -> advertisingProps.ActiveInstances <- e :?> _)
            trySet values "SupportedInstances" (fun e -> advertisingProps.SupportedInstances <- e :?> _)
            trySet values "SupportedIncludes" (fun e -> advertisingProps.SupportedIncludes <- e :?> _)
            Some advertisingProps
        | _ -> None
                    
    let device =
        match kv.TryGetValue "org.bluez.Device1" with
        | true, values ->
            let deviceProps = Device1Properties()
            trySet values "Address" (fun e -> deviceProps.Address <- e :?> _)
            trySet values "AddressType" (fun e -> deviceProps.AddressType <- e :?> _)
            trySet values "Name" (fun e -> deviceProps.Name <- e :?> _)
            trySet values "Alias" (fun e -> deviceProps.Alias <- e :?> _)
            trySet values "Appearance" (fun e -> deviceProps.Appearance <- e :?> _)
            trySet values "Paired" (fun e -> deviceProps.Paired <- e :?> _)
            trySet values "Trusted" (fun e -> deviceProps.Trusted <- e :?> _)
            trySet values "Blocked" (fun e -> deviceProps.Blocked <- e :?> _)
            trySet values "LegacyPairing" (fun e -> deviceProps.LegacyPairing <- e :?> _)
            trySet values "Connected" (fun e -> deviceProps.Connected <- e :?> _)
            trySet values "UUIDs" (fun e -> deviceProps.UUIDs <- e :?> _)
            trySet values "Adapter" (fun e -> deviceProps.Adapter <- e :?> _)
            trySet values "ServicesResolved" (fun e -> deviceProps.ServicesResolved <- e :?> _)
            Some deviceProps
        | _ -> None
    
    // TODO: print info if unknown interfaces appear here!

    { Path = key
      AdapterProps = adapter
      LEAdvertisingProps = leadvertising
      DeviceProps = device }

let connectInterface (dispatch:Dispatch<Message>) =
    async {
        try
            let! serviceName = async {
                if isServer then
                    let! c = dbusBus.ConnectAsync() |> Async.AwaitTask
                    return c.LocalName
                else
                    do! dbusBus.RegisterServiceAsync("de.ilamp") |> Async.AwaitTask
                    return "de.ilamp"
            }
            
            let agent = new MyAgent()
            do! dbusBus.RegisterObjectAsync(agent) |> Async.AwaitTask
            let agentMgr = dbusBus.CreateProxy<IAgentManager1>(bluezName, ObjectPath "/org/bluez")
            do! agentMgr.RegisterAgentAsync(agent.ObjectPath, "KeyboardDisplay") |> Async.AwaitTask
            do! agentMgr.RequestDefaultAgentAsync(agent.ObjectPath) |> Async.AwaitTask
            let manager = dbusBus.CreateProxy<IObjectManager>(bluezName, ObjectPath.Root)
            let! d =
                manager.WatchInterfacesAddedAsync(
                    (fun struct (path, dict) ->
                        dispatch (ObjectConnected (getDevice path dict))),
                    (fun exn ->
                        System.Console.WriteLine("Error while adding interface: {0}", exn)))
                    |> Async.AwaitTask
            let! d =
                manager.WatchInterfacesRemovedAsync(
                    (fun struct (path, dict) ->
                        dispatch (ObjectDisconnected (path))),
                    (fun exn ->
                        System.Console.WriteLine("Error while removing interface: {0}", exn)))
                    |> Async.AwaitTask
            let! d = manager.GetManagedObjectsAsync() |> Async.AwaitTask
            let connectInfo =
                d
                |> Seq.map (fun kv -> getDevice kv.Key kv.Value)
                |> Seq.toList
            dispatch (ConnectedDBus(manager, connectInfo))
        with e ->
            dispatch (ShowError (sprintf "Connect DBUS failed: %A" e))
    }
    |> Async.Start

let connectDevice (p:ObjectPath) (dispatch:Dispatch<Message>) =
    async {
        try
            let device = dbusBus.CreateProxy<IDevice1>(bluezName, p)
            dispatch (ShowProgress "Trying to connect ....")
            do! device.ConnectAsync() |> Async.AwaitTask
            dispatch (ShowOk "Device connected!")
        with e ->
            dispatch (ShowError (sprintf "Device connection failed: %A" e))
    }
    |> Async.Start
    

let disconnectDevice (p:ObjectPath) (dispatch:Dispatch<Message>) =
    async {
        try
            let device = dbusBus.CreateProxy<IDevice1>(bluezName, p)
            dispatch (ShowProgress "Trying to disconnect ....")
            do! device.DisconnectAsync() |> Async.AwaitTask
            dispatch (ShowOk "Device disconnect!")
        with e ->
            dispatch (ShowError (sprintf "Device connection failed: %A" e))
    }
    |> Async.Start

let startDiscover (p:ObjectPath) (dispatch:Dispatch<Message>) =
    async {
        try
            let adapter = dbusBus.CreateProxy<IAdapter1>(bluezName, p)
            do! adapter.StartDiscoveryAsync() |> Async.AwaitTask
            dispatch (ShowOk "Discover started!")
        with e ->
            dispatch (ShowError (sprintf "Adapter discover failed: %A" e))
    }
    |> Async.Start

let getDeviceProperties (p:ObjectPath) (dispatch:Dispatch<Message>) =
    async {
        try
            let device = dbusBus.CreateProxy<IDevice1>(bluezName, p)
            dispatch (ShowProgress "Trying to pair ....")
            let! props = device.GetAllAsync() |> Async.AwaitTask
            dispatch (UpdateDeviceProperties(p, props))
        with e ->
            dispatch (ShowError (sprintf "Device connection failed: %A" e))
    }
    |> Async.Start
    
let startProfileStream (uuidPrefix:string) (model:Model) (dispatch:Dispatch<Message>) =
    async {
        try
            match model.CurrentDevice with
            | None -> dispatch (ShowError "No device selected!")
            | Some (p, data) ->
                let device = dbusBus.CreateProxy<IDevice1>(bluezName, p)
                
                // resolve uuid
                let! uuid =
                    async {
                        let fromCache =
                            match data with
                            | Some dev ->
                                dev.UUIDs |> Seq.tryFind(fun u -> u.StartsWith uuidPrefix)
                            | None -> None
                        match fromCache with
                        | Some c -> return Some c
                        | None ->
                            // Retrieve latest data
                            let! props = device.GetAllAsync() |> Async.AwaitTask
                            dispatch (UpdateDeviceProperties(p, props))
                            return props.UUIDs |> Seq.tryFind(fun u -> u.StartsWith uuidPrefix)
                    }
                
                match uuid with
                | None -> dispatch (ShowError (sprintf "Could not find uuid '%s'!" uuidPrefix))
                | Some uuid ->
                    // register dbus if needed
                    let! profile =
                        async {
                            match model.DBusConnectedProfiles |> Map.tryFind uuid with
                            | Some s -> return s
                            | None ->
                                dispatch (ShowProgress "Creating and registering new profile (dbus).")
                                let profile = new MyProfile(dispatch, uuid)
                                do! dbusBus.RegisterObjectAsync(profile) |> Async.AwaitTask
                                dispatch (ShowProgress "Register with profile manager.")
                                let profileManager = dbusBus.CreateProxy<IProfileManager1>(bluezName, ObjectPath "/org/bluez")
                                let dic = new Dictionary<string, obj>()
                                //dic.["Channel"] <- uint16 0us
                                //dic.["AutoConnect"] <- true
                                //dic.["Role"] <- "client"
                                //dic.["Name"] <- "SerialPort"
                                //dic.["Service"] <- "00000003-0000-1000-8000-00805F9B34FB" // RFCOMM, see https://www.bluetooth.com/specifications/assigned-numbers/service-discovery
                                do! profileManager.RegisterProfileAsync(profile.ObjectPath, uuid, dic) |> Async.AwaitTask
                                //profileManager.RegisterProfileAsync()
                                dispatch (ProfileRegistered(uuid, profile))
                                return profile
                        }
                    
                    dispatch (ShowProgress "Connecting profile.")
                    do! device.ConnectProfileAsync(uuid) |> Async.AwaitTask
                    dispatch (ShowProgress "Device profile connected.")
        with e ->
            dispatch (ShowError (sprintf "Device connection failed: %A" e))
    }
    |> Async.Start

let pairDevice (p:ObjectPath) (dispatch:Dispatch<Message>) =
    async {
        try
            let device = dbusBus.CreateProxy<IDevice1>(bluezName, p)
            do! device.PairAsync() |> Async.AwaitTask
            do! device.SetAsync("Trusted", true) |> Async.AwaitTask
            dispatch (ShowOk "Device paired!")
        with e ->
            dispatch (ShowError (sprintf "Device pairing failed: %A" e))
    }
    |> Async.Start

let unpairDevice (p:ObjectPath) (dispatch:Dispatch<Message>) =
    async {
        try
            let device = dbusBus.CreateProxy<IDevice1>(bluezName, p)
            let! adapterPath = device.GetAdapterAsync() |> Async.AwaitTask
            let adapter = dbusBus.CreateProxy<IAdapter1>(bluezName, adapterPath)
            do! adapter.RemoveDeviceAsync(p) |> Async.AwaitTask
            dispatch (ShowOk "Device deleted/unpaired!")
        with e ->
            dispatch (ShowError (sprintf "Device deletion/unpairing failed: %A" e))
    }
    |> Async.Start


let parseInput (model:Model) (i:string) =
    let splits = i.Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
    if splits.Length = 0 then
        PrintPrompt
    else

    let command = splits.[0]

    let tryParseItem s =
        match s with
        | "adapter" | "a" ->
            Some SelectItemType.Adapter
        | "device" | "dev" ->
            Some SelectItemType.Device
        | "stream" | "io" ->
            Some SelectItemType.Stream
        | _ ->
            None
        
    let item =
        if splits.Length > 1 then
            (tryParseItem splits.[1])
        else (if model.SelectedDevice.IsSome then Some SelectItemType.Device else Some SelectItemType.Adapter)
    match command, item with
    | "exit", _ | "quit", _ when splits.Length = 1 ->
        ApplicationExit
    | "list", Some dev | "l", Some dev when splits.Length = 1 || splits.Length = 2 ->
        PrepareList dev
    | "detail", Some dev | "details", Some dev | "d", Some dev when splits.Length = 1 || splits.Length = 2 ->
        PrepareDetails dev
    | "select", Some dev when splits.Length = 3 ->
        let findStr = splits.[2]
        let inline from constr devType opt =
            match opt with
            | None -> CouldNotFindItem (devType, findStr)
            | Some item ->
                constr(item)
        match dev with
        | SelectItemType.Adapter ->
            let idFind =
                model.AvailableObjects
                |> Seq.filter (fun item -> item.IsAdapter)
                |> Seq.mapi (fun i item -> i, item.Path, item)
                |> Seq.tryFind (fun (i, path, item) -> (string i) = findStr)
            let pathFind =
                model.AvailableObjects
                |> Seq.filter (fun item -> item.IsAdapter)
                |> Seq.mapi (fun i item -> i, item.Path, item)
                |> Seq.tryFind (fun (i, path, item) -> (string path).EndsWith findStr)
            Option.orElse pathFind idFind 
            |> Option.map (fun (i, path, item) -> path)
            |> from SelectAdapter SelectItemType.Adapter
        | SelectItemType.Device ->
            let idFind =
                model.AvailableObjects
                |> Seq.filter (fun item -> item.IsDevice)
                |> Seq.mapi (fun i item -> i, item.Path, item)
                |> Seq.tryFind (fun (i, path, item) -> (string i) = findStr)
            let pathFind =
                model.AvailableObjects
                |> Seq.filter (fun item -> item.IsDevice)
                |> Seq.mapi (fun i item -> i, item.Path, item)
                |> Seq.tryFind (fun (i, path, item) -> (string path).EndsWith findStr)
            Option.orElse pathFind idFind 
            |> Option.map (fun (i, path, item) -> path)
            |> from SelectDevice SelectItemType.Device
        | SelectItemType.Stream ->
            let idFind =
                model.AvailableStreams
                |> Seq.mapi (fun i item -> i, item)
                |> Seq.tryFind (fun (i, item) -> (string i) = findStr)
            let uuidFind =
                model.AvailableStreams
                |> Seq.mapi (fun i item -> i, item)
                |> Seq.tryFind (fun (i, item) -> item.UUID.StartsWith findStr)
            Option.orElse uuidFind idFind
            |> Option.map snd
            |> from SelectStream SelectItemType.Stream
    | "startStream", _ when splits.Length = 2 ->
        ConnectProfileStream splits.[1]
    | "connect", _ when splits.Length = 1 ->
        ConnectDevice
    | "disconnect", _ when splits.Length = 1 ->
        DisconnectDevice
    | "pair", _ when splits.Length = 1 ->
        PairDevice
    | "unpair", _ | "delete", _ when splits.Length = 1 ->
        // https://stackoverflow.com/questions/35999773/dbus-bluez5-cancelpairing
        UnpairDevice
    | "send", _ | "s", _ when splits.Length = 2 ->
        // parse 30:31:32:33:34:35:36:37
        match tryParseHex splits.[1] with
        | Some h -> SendBytes h
        | _ -> InvalidUsage i
    | "discover", _ when splits.Length = 1 -> StartDiscover
    | "help", _ -> PrintHelp
    | _ when String.IsNullOrEmpty i ->
        PrintPrompt
    | _ ->
        InvalidUsage i




let init startMsgs () =
    { ObjectManager = None
      DBusConnectedProfiles = Map.empty
      AvailableObjects = [||]
      AvailableStreams = [||]
      SelectedAdapter = None
      SelectedDevice = None
      SelectedStream = None
      PreviousModel = None }, 
    Cmd.batch [
        Cmd.ofSub readInputs
        Cmd.ofSub connectInterface
        startMsgs
        ]


let update message model =
    let newModel, cmd =
        match message with
        | ApplicationExit ->
            applicationExit.Cancel()
            model, Cmd.none
        | UserInput i ->
            model, Cmd.ofMsg (parseInput model i)
        | SelectAdapter path ->
            { model with SelectedAdapter = Some path }, Cmd.none
        | SelectDevice path ->
            { model with SelectedDevice = Some path }, Cmd.none
        | ProfileRegistered(uuid, profile) ->
            { model with DBusConnectedProfiles = model.DBusConnectedProfiles |> Map.add uuid profile }, Cmd.none
        | NewConnectedStream(stream) ->
            { model with AvailableStreams = Array.append model.AvailableStreams [|stream|]; SelectedStream = Some stream }, Cmd.none
        | DisconnectedStream (stream) ->
            { model with
                AvailableStreams = model.AvailableStreams |> Array.filter (fun s -> not <| Object.ReferenceEquals(s, stream))
                SelectedStream = 
                    match model.SelectedStream with
                    | Some s when Object.ReferenceEquals(s, stream) -> None
                    | _ -> model.SelectedStream }, Cmd.none
        | SendBytes (data) ->
            match model.SelectedStream with
            | Some s -> s.StartSend(data)
            | None -> ()
            model, Cmd.none
        | PrepareDetails tp ->
            let retrieveDetails (dispatch:Dispatch<Message>) =
                async {
                    match tp with
                    | SelectItemType.Device ->
                        match model.SelectedDevice with
                        | None -> dispatch (ShowError "No device selected!")
                        | Some p -> getDeviceProperties p dispatch
                    | _ -> dispatch (ShowError "Not implemented!")
                }
                |> Async.Start

            model, Cmd.ofSub retrieveDetails
        | ConnectProfileStream uuidPrefix ->
            model, Cmd.ofSub (startProfileStream uuidPrefix model)
        | ConnectDevice ->
            let cmd =
                match model.CurrentDevice with
                | None -> ShowError "No device selected." |> Cmd.ofMsg
                | Some (path, None) -> ShowError (sprintf "Selected device '%O' no longer available." path) |> Cmd.ofMsg
                | Some (path, Some (_)) ->
                    Cmd.ofSub (connectDevice path)
            model, cmd
        | DisconnectDevice ->
            let cmd =
                match model.CurrentDevice with
                | None -> ShowError "No device selected." |> Cmd.ofMsg
                | Some (path, None) -> ShowError (sprintf "Selected device '%O' no longer available." path) |> Cmd.ofMsg
                | Some (path, Some (_)) ->
                    Cmd.ofSub (disconnectDevice path)
            model, cmd
        | PairDevice ->
            let cmd =
                match model.CurrentDevice with
                | None -> ShowError "No device selected." |> Cmd.ofMsg
                | Some (path, None) -> ShowError (sprintf "Selected device '%O' no longer available." path) |> Cmd.ofMsg
                | Some (path, Some (_)) ->
                    Cmd.ofSub (pairDevice path)
            model, cmd
        | UnpairDevice ->
            let cmd =
                match model.CurrentDevice with
                | None -> ShowError "No device selected." |> Cmd.ofMsg
                | Some (path, None) -> ShowError (sprintf "Selected device '%O' no longer available." path) |> Cmd.ofMsg
                | Some (path, Some (_)) ->
                    Cmd.ofSub (unpairDevice path)
            model, cmd
        | StartDiscover ->
            let cmd =
                match model.CurrentAdapter with
                | None -> ShowError "No adapter selected." |> Cmd.ofMsg
                | Some (path, None) -> ShowError (sprintf "Selected adapter '%O' no longer available." path) |> Cmd.ofMsg
                | Some (path, Some (_)) ->
                    Cmd.ofSub (startDiscover path)
            model, cmd
        | ConnectedDBus (mgr, devices) ->
            { model with ObjectManager = Some mgr }, 
            let msgs =
                devices
                |> List.map (ObjectConnected >> Cmd.ofMsg)
            (msgs @ [ Cmd.ofMsg ConnectionOK ])
            |> Cmd.batch
        | ObjectConnected device ->
            let newAvailableDevices =
                match model.AvailableObjects |> Seq.tryFindIndex (fun d -> d.Path = device.Path) with
                | Some idx ->
                    model.AvailableObjects
                    |> Array.mapi (fun i item -> if i = idx then device else item)
                | None ->
                    Array.append model.AvailableObjects [|device|]
            { model with AvailableObjects = newAvailableDevices }, Cmd.none
        | ObjectDisconnected path ->
            let newAvailableDevices =
                model.AvailableObjects
                |> Array.filter (fun item -> item.Path <> path)
            let newSelectedDevice =
                if model.SelectedDevice = Some path then None else model.SelectedDevice
            let newSelectedAdapter =
                if model.SelectedAdapter = Some path then None else model.SelectedAdapter
            let newSelectedStream =
                match model.SelectedStream with
                | Some s when s.Device = path ->
                    None
                | _ -> model.SelectedStream
            { model with 
                AvailableObjects = newAvailableDevices
                SelectedAdapter = newSelectedAdapter
                SelectedDevice = newSelectedDevice
                SelectedStream = newSelectedStream }, Cmd.none
        | _ -> model, Cmd.none
    
    { newModel with PreviousModel = Some (message, { model with PreviousModel = None }) }, cmd

let helpString = """Help:

items:
  adapter/a    bluetooth adapter
  device/dev   bluetooth device within an adapter
  stream/io    profile/stream of a device

operations:
  list/l <item>       list available <item>s (defaults to 'device' if one is selected or 'adapter' otherwise)
  detail/d <item>     show details for the currently selected <item> (defaults to 'device' if one is selected or 'adapter' otherwise)
  select <item> <id>  select the given <item> with the current <id>
  startStream <uuid>  connect the currently selected device and open a stream for the given profile, and selects it as current stream
  connect             connect the currently selected device
  pair                pair the currently selected device
  unpair/delete       remove the selected device, deletes pairing information
  send/s <hexdata>    send the given data to the currently selected stream
  discover            start device discovery for the currently selected adapter
  exit/quit           exit the application

"""
let view model dispatch =
    let mutable disablePrompt =
        if applicationExit.Token.IsCancellationRequested then true
        else false
    match model.PreviousModel with
    | Some (PrintHelp, _) -> Console.WriteLine (helpString)
    | Some (ShowError err, _) ->
        Console.WriteLine (sprintf "Error: %s" err)
    | Some (ReceivedBytes(stream, data), _) ->
        Console.WriteLine (sprintf "Received Data '%O/%s': %A" stream.Device stream.UUID (toHexString true (Memory<byte>.op_Implicit data.Memory)))
        data.Dispose()
        disablePrompt <- true
    | Some (ShowOk msg, _) ->
        Console.WriteLine (msg)
    | Some (ShowProgress msg, _) ->
        Console.WriteLine (msg)
        disablePrompt <- true
    | Some (PrintPrompt, _) -> ()
    | Some (InvalidUsage u, _) ->
        Console.WriteLine ("Could not detect any command from '{0}', please write 'help' if you need help.", u)
    | Some (CouldNotFindItem(typ, i), _) ->
        Console.WriteLine (sprintf "Could not find any %A with on position/with name '%s'." typ i)
    | Some (ConnectionOK, _) -> Console.WriteLine ("DBus Connection established.")
    | Some (SelectAdapter path, _) -> Console.WriteLine (sprintf "Adapter '%O' selected." path)
    | Some (SelectDevice path, _) -> Console.WriteLine (sprintf "Device '%O' selected." path)
    
    | Some (PrepareList SelectItemType.Device, _) ->
        Console.WriteLine("Devices:")
        model.AvailableObjects
            |> Seq.choose (fun i -> i.DeviceProps |> Option.map (fun dev -> i.Path, dev.AsDictionary()))
            |> Seq.mapi (fun i (path, props) -> (i, path), props)
            |> dict
            |> ReflectionBased.printValue
            |> printfn "%s"
    | Some (PrepareList SelectItemType.Adapter, _) ->
        Console.WriteLine("Adapters:")
        model.AvailableObjects
            |> Seq.choose (fun i -> i.AdapterProps |> Option.map (fun adp -> i.Path, adp.AsDictionary()))
            |> Seq.mapi (fun i (path, props) -> (i, path), props)
            |> dict
            |> ReflectionBased.printValue
            |> printfn "%s"
    | Some (PrepareList SelectItemType.Stream, _) ->
        Console.WriteLine ("Streams:")
        model.AvailableStreams
            |> Seq.mapi (fun idx s ->
                (idx, s.UUID),
                [ "Device", string s.Device
                  "UUID", s.UUID
                  "Profile", string s.Profile.ObjectPath ] |> dict)
            |> dict
            |> ReflectionBased.printValue
            |> printfn "%s"
            
    | Some (UpdateDeviceProperties (path, props), _) ->
        Console.WriteLine (sprintf "Device '%O' details:" path)
        props.AsDictionary()
        |> ReflectionBased.printValue
        |> printfn "%s"
    | Some (PrepareDetails SelectItemType.Adapter, _) ->
        match model.SelectedAdapter with
        | Some p ->
            match model.TryFindAdapter p with
            | Some dev ->
                Console.WriteLine (sprintf "Adapter '%O' details:" p)
                dev.AdapterProps.Value.AsDictionary()
                |> ReflectionBased.printValue
                |> printfn "%s"
            | None -> Console.WriteLine (sprintf "Currently selected adapter '%O' not found" p)
        | None -> Console.WriteLine (sprintf "Currently no adapter selected!")
    | Some (PrepareDetails SelectItemType.Stream, _) -> Console.WriteLine (sprintf "Stream '%O' details:" model.SelectedStream)
    | Some (ObjectConnected device, _) ->
        match device.AdapterProps with
        | Some ad ->
            Console.WriteLine (sprintf "Adapter '%s' (%s) found, Path: '%O'!" ad.Name ad.Address device.Path)
        | None -> ()
        match device.DeviceProps with
        | Some dev ->
            Console.WriteLine (sprintf "Device '%s' (%s) found, Path: '%O'!" dev.Name dev.Address device.Path)
        | None -> ()
        disablePrompt <- true
    | Some (ObjectDisconnected path, _) ->
        Console.WriteLine (sprintf "Device '%O' disconnected!" path)

        disablePrompt <- true
        
    | Some (ApplicationExit, _) ->
        Console.WriteLine ("Exiting...")
        disablePrompt <- true
    | Some (_, _) ->
        disablePrompt <- true
        ()
    | None ->
        Console.WriteLine ("Trying to connect to the dbus system. Enter 'help' to get help.")
        Console.Write("> ")

    if not disablePrompt then Console.Write("> ")

let getAdapters (con:Connection) (manager:IObjectManager) =
    async{
        let! d = manager.GetManagedObjectsAsync() |> Async.AwaitTask
        return
            d
            |> Seq.toList
            |> List.map (fun kv -> kv.Key, kv.Value)
            |> List.filter (fun (key, value) ->
                value.ContainsKey "org.bluez.Adapter1")
            |> List.map (fun (key, value) ->
                (key, value), lazy con.CreateProxy<IAdapter1>(bluezName, key), lazy con.CreateProxy<IGattManager1>(bluezName, key))
    }

    
/// Trace all the updates to the console
let withConsoleTrace (program: Program<'arg, 'model, 'msg, 'view>) =
    let traceInit (arg:'arg) =
        let initModel,cmd = program.init arg
        //printfn "Initial state: %A" initModel
        initModel,cmd

    let traceUpdate msg model =
        printfn "New message: %A" msg
        let newModel,cmd = program.update msg model
        //printfn "Updated state: %A" newModel
        newModel,cmd

    { program with
        init = traceInit 
        update = traceUpdate
        onError = fun (msg, exn) -> printfn "%s -> %A" msg exn}

[<EntryPoint>]
let main argv =

    let startMsgs =
        Cmd.ofSub (fun dispatch ->
            async {
                do! Async.Sleep 2000
                dispatch (UserInput "select adapter 0")
                do! Async.Sleep 200
                dispatch (UserInput "select device /org/bluez/hci0/dev_C9_A3_05_FE_BD_41")
                do! Async.Sleep 200
                dispatch (UserInput "startStream 00001101-0000-1000-8000-00805f9b34fb")
            }
            |> Async.Start
        ) 

    Program.mkProgram (init startMsgs) update view
    |> withConsoleTrace
    |> Program.run

    printfn "waiting for token... "
    applicationExit.Token.WaitHandle.WaitOne() |> ignore
    
    0 // return an integer exit code
