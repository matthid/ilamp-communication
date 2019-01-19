﻿// Learn more about F# at http://fsharp.org

open System
open bluez.DBus
open Tmds.DBus
open System.IO
open Elmish
open System.Threading
open System.Collections.Generic
open System.Threading.Tasks

(*
type DemoAgent () =
    interface Agent
    *)
type MyProfile () =
    interface IProfile1 with
        member x.Release() = ()
        member x.NewConnection(device, fd, properties) =
            let stream = new FileStream(fd, FileAccess.ReadWrite)
            stream.Write([| 8uy |], 0, 1)
            System.Console.WriteLine(sprintf "new connection %O => %A" device properties)
            ()
        member x.RequestDisconnection(device) =
            ()

type ConnectedStream =
    { Profile : MyProfile
      Adapter : ObjectPath
      Device : ObjectPath
      UUID : string}

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

type Message =
    | PrepareList of SelectItemType
    | PrepareDetails of SelectItemType
    | SelectAdapter of ObjectPath
    | SelectDevice of ObjectPath
    | SelectStream of ConnectedStream
    | ConnectDevice
    | PairDevice
    | ConnectProfileStream of uuid:string // selected device
    | UpdateDeviceProperties of ObjectPath * Device1Properties
    | ListAdapterProperties of ObjectPath
    | UserInput of string
    | SendBytes of byte[]
    | ReceivedBytes of ConnectedStream * byte[]
    | StartDiscover
    | ObjectConnected of ObjectInfo
    | ObjectDisconnected of ObjectPath
    | ConnectedDBus of IObjectManager * ObjectInfo list

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
      AvailableStreams : ConnectedStream[]
      SelectedAdapter : ObjectPath option
      SelectedDevice : ObjectPath option
      SelectedStream : ConnectedStream option
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
let dbusBus = Connection.System
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

module ReflectionBased =
    let rec printEnumerable (d:System.Collections.IEnumerable) : string =
        "[" + String.Join(";\n",
            d |> Seq.cast<obj> |> Seq.map (fun k -> printValue k)) + " ]"
    and printKeyValuePair (k, v) =
        sprintf "  %A ->\n    %s" k ((printValue v).Replace("\n", "\n    "))
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

let pairDevice (p:ObjectPath) (dispatch:Dispatch<Message>) =
    async {
        try
            let device = dbusBus.CreateProxy<IDevice1>(bluezName, p)
            do! device.PairAsync() |> Async.AwaitTask
            dispatch (ShowOk "Device paired!")
        with e ->
            dispatch (ShowError (sprintf "Device connection failed: %A" e))
    }
    |> Async.Start

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

    let res = Array.zeroCreate byteLength
    let multiplier = if withDel then 3 else 2
    for i in 0 .. byteLength-1 do 
        let byteValue = s.Substring(i * multiplier, 2)
        assert (withDel && i > 0 && s.[i * multiplier - 1] = ':')
        res.[i] <- System.Byte.Parse(byteValue, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture)
    res

let tryParseHex (sin:string) =
    try Some (parseHex sin) with _ -> None

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
    | "pair", _ when splits.Length = 1 ->
        PairDevice
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




let init () =
    { ObjectManager = None
      AvailableObjects = [||]
      AvailableStreams = [||]
      SelectedAdapter = None
      SelectedDevice = None
      SelectedStream = None
      PreviousModel = None }, 
    Cmd.batch [
        Cmd.ofSub readInputs
        Cmd.ofSub connectInterface
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
        | PrepareDetails tp ->
            let retrieveDetails (dispatch:Dispatch<Message>) =
                async {
                    match tp with
                    | SelectItemType.Device ->
                        match model.SelectedDevice with
                        | None -> dispatch (ShowError "No device selected!")
                        | Some p -> getDeviceProperties p dispatch
                    | _ -> ()
                }
                |> Async.Start

            model, Cmd.ofSub retrieveDetails
        | ConnectDevice ->
            let cmd =
                match model.CurrentDevice with
                | None -> ShowError "No device selected." |> Cmd.ofMsg
                | Some (path, None) -> ShowError (sprintf "Selected device '%O' no longer available." path) |> Cmd.ofMsg
                | Some (path, Some (_)) ->
                    Cmd.ofSub (connectDevice path)
            model, cmd
        | PairDevice ->
            let cmd =
                match model.CurrentDevice with
                | None -> ShowError "No device selected." |> Cmd.ofMsg
                | Some (path, None) -> ShowError (sprintf "Selected device '%O' no longer available." path) |> Cmd.ofMsg
                | Some (path, Some (_)) ->
                    Cmd.ofSub (pairDevice path)
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
                | Some s when s.Adapter = path || s.Device = path ->
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
    

    Program.mkProgram init update view
    |> withConsoleTrace
    |> Program.run

    printfn "waiting for token... "
    applicationExit.Token.WaitHandle.WaitOne() |> ignore
    
    0 // return an integer exit code