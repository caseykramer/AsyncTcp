module Client
#nowarn "40"

open Buffer
open Socket
open System
open System.Net
open System.Net.Sockets

type Client(?rcvBufferSize:int) =
    let receiveBufferSize = defaultArg rcvBufferSize 64
    let readWritePool = new ArgsPool(2) // One send, one receive
    let bufferManager = new BufferManager(receiveBufferSize*2,receiveBufferSize)
    let mutable socket:Socket Disposable = Disposed

    let doDisconnect socket = 
        async {
            return! Socket.Disconnect readWritePool socket
        }

    let rec doReceive socket = 
        async {
            Log.debugf "Starting receive from server"
            let! data = Socket.Receive readWritePool socket bufferManager
            match data with
            | None -> return None
            | Some data ->
                Log.debugf "Received %i bytes from server" data.Length
                return Some data
        }

    let rec doSend (data:byte [])= 
        async {
            Log.debugf "Starting send of %i bytes to server" data.Length
            let! sent = Socket.Send readWritePool socket bufferManager data
            match sent with
            | None -> ignore()
            | Some sent ->
                Log.debugf "Sent %i bytes to server" data.Length
                if sent > data.Length
                    then return! doSend data.[sent..]
                    else ignore() }

    let doConnect (endPoint:EndPoint) = 
        async {
            let s = new Socket(endPoint.AddressFamily,SocketType.Stream,ProtocolType.Tcp)
            let! skt = Socket.Connect readWritePool (Active s) endPoint
            match skt with
            | None -> ignore()
            | Some skt -> socket <- skt }
    

    let getEndPoint host port =
        let addresses = Dns.GetHostAddresses(host) |> List.ofArray
        match addresses with
        | [] -> failwith "Unable to find address for host %s" host
        | _ -> match addresses |> List.tryFind (fun (a:IPAddress) -> a.AddressFamily = AddressFamily.InterNetwork) with
               | Some addr -> IPEndPoint(addr,port)
               | None -> IPEndPoint(addresses.Head,port)
        
    member __.ConnectAsync(host,port) =
        let endpoint = getEndPoint host port
        doConnect endpoint
    member __.Connect(host,port) =
        let endpoint = getEndPoint host port
        let s = new Socket(endpoint.AddressFamily,SocketType.Stream,ProtocolType.Tcp)
        s.Connect(endpoint)
        socket <- Active s

    member __.DisconnectAsync() =
        doDisconnect socket
    member __.Disconnect() =
        doDisconnect socket |> Async.RunSynchronously

    member __.ReceieveAsync() = 
        doReceive socket
    member __.ReceiveSync() =
        doReceive socket |> Async.RunSynchronously
    
    member __.SendAsync(data) =
        doSend data
    
    member __.Send(data) =
        doSend data |> Async.RunSynchronously
    interface IDisposable with
        member __.Dispose() = 
            __.Disconnect()
            match socket with
            | Active s ->
                    s.Disconnect(false)
                    s.Close()
                    socket <- socket.Dispose()
            | Disposed -> ignore()