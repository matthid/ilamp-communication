// Learn more about F# at http://fsharp.org

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

type DeviceInfo =
    { Path : ObjectPath
      AdapterProps : Adapter1Properties option
      LEAdvertisingProps : LEAdvertisingManager1Properties option
      DeviceProps : Device1Properties option }

type Message =
    | PrepareList of SelectItemType
    | PrepareDetails of SelectItemType
    | SelectAdapter of ObjectPath
    | SelectDevice of ObjectPath
    | SelectStream of ConnectedStream
    | ConnectProfileStream of uuid:string // selected device
    | ListDeviceProperties of ObjectPath
    | ListAdapterProperties of ObjectPath
    | UserInput of string
    | SendBytes of byte[]
    | ReceivedBytes of ConnectedStream * byte[]
    | StartDiscover
    | DeviceConnected of DeviceInfo
    | DeviceDisconnected of ObjectPath
    | ConnectedDBus of IObjectManager * DeviceInfo list

    // Outputs
    | InvalidUsage of string
    | CouldNotFindItem of SelectItemType * string
    | PrintPrompt
    | PrintHelp
    | ApplicationExit
    | ConnectionOK

type Model =
    { ObjectManager : IObjectManager option
      AvailableAdapters : ObjectPath[]
      
      AvailableDevices : DeviceInfo[]
      AvailableStreams : ConnectedStream[]
      SelectedAdapter : ObjectPath option
      SelectedDevice : ObjectPath option
      SelectedStream : ConnectedStream option
      PreviousModel : (Message * Model) option }


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
    let rec printIDict (d:System.Collections.IDictionary) : string =
        "[\n" + String.Join(";\n",
            d.Keys |> Seq.cast<obj> |> Seq.map (fun k -> sprintf "  %A ->\n    %s" k ((printValue d.[k]).Replace("\n", "\n    ")))) + " ]"
    and printValue (a) : string =
        match a with
        | :? System.Collections.IDictionary as d -> printIDict d
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
                    dispatch (DeviceConnected (getDevice path dict))),
                (fun exn ->
                    System.Console.WriteLine("Error while adding interface: {0}", exn)))
                |> Async.AwaitTask
        let! d =
            manager.WatchInterfacesRemovedAsync(
                (fun struct (path, dict) ->
                    dispatch (DeviceDisconnected (path))),
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
    | "detail", Some dev | "d", Some dev when splits.Length = 1 || splits.Length = 2 ->
        PrepareList dev
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
                model.AvailableAdapters
                |> Seq.mapi (fun i item -> i, item)
                |> Seq.tryFind (fun (i, item) -> (string i) = findStr)
            let pathFind =
                model.AvailableAdapters
                |> Seq.mapi (fun i item -> i, item)
                |> Seq.tryFind (fun (i, item) -> (string item).EndsWith findStr)
            Option.orElse pathFind idFind 
            |> Option.map snd
            |> from SelectAdapter SelectItemType.Adapter
        | SelectItemType.Device ->
            let idFind =
                model.AvailableDevices
                |> Seq.mapi (fun i item -> i, item.Path)
                |> Seq.tryFind (fun (i, item) -> (string i) = findStr)
            let pathFind =
                model.AvailableDevices
                |> Seq.mapi (fun i item -> i, item.Path)
                |> Seq.tryFind (fun (i, item) -> (string item).EndsWith findStr)
            Option.orElse pathFind idFind 
            |> Option.map snd
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
    | "connect", _ when splits.Length = 2 ->
        ConnectProfileStream splits.[1]
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
      AvailableAdapters = [||]
      AvailableDevices = [||]
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
        | ConnectedDBus (mgr, devices) ->
            { model with ObjectManager = Some mgr }, 
            let msgs =
                devices
                |> List.map (DeviceConnected >> Cmd.ofMsg)
            (msgs @ [ Cmd.ofMsg ConnectionOK ])
            |> Cmd.batch
        | DeviceConnected device ->
            let newAvailableDevices =
                match model.AvailableDevices |> Seq.tryFindIndex (fun d -> d.Path = device.Path) with
                | Some idx ->
                    model.AvailableDevices
                    |> Array.mapi (fun i item -> if i = idx then device else item)
                | None ->
                    Array.append model.AvailableDevices [|device|]
            { model with AvailableDevices = newAvailableDevices }, Cmd.none
        | _ -> model, Cmd.none
    
    { newModel with PreviousModel = Some (message, { model with PreviousModel = None }) }, cmd

let helpString = """Help:

items:
  adapter/a    bluetooth adapter
  device/dev   bluetooth device within an adapter
  stream/io     profile/stream of a device

operations:
  list/l <item>       list available <item>s (defaults to 'device' if one is selected or 'adapter' otherwise)
  detail/d <item>     show details for the currently selected <item> (defaults to 'device' if one is selected or 'adapter' otherwise)
  select <item> <id>  select the given <item> with the current <id>
  connect <uuid>      connect the currently selected device and open a stream for the given profile, and selects it as current stream
  send/s <hexdata>      send the given data to the currently selected stream
  discover            start device discovery for the currently selected adapter
  exit/quit           exit the application

"""
let view model dispatch =
    let mutable disablePrompt =
        if applicationExit.Token.IsCancellationRequested then true
        else false
    match model.PreviousModel with
    | Some (PrintHelp, _) -> Console.WriteLine (helpString)
    | Some (PrintPrompt, _) -> ()
    | Some (InvalidUsage u, _) ->
        Console.WriteLine ("Could not detect any command from '{0}', please write 'help' if you need help.", u)
    | Some (CouldNotFindItem(typ, i), _) ->
        Console.WriteLine (sprintf "Could not find any %A with on position/with name '%s'." typ i)
    | Some (ConnectionOK, _) -> Console.WriteLine ("DBus Connection established.")
    | Some (DeviceConnected device, _) ->
        match device.AdapterProps with
        | Some ad ->
            Console.WriteLine (sprintf "Adapter '%s' (%s) found!" ad.Name ad.Address)
        | None -> ()
        match device.DeviceProps with
        | Some dev ->
            Console.WriteLine (sprintf "Device '%s' (%s) found!" dev.Name dev.Address)
        | None -> ()
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
