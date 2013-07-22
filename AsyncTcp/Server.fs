module Server

open Socket
open Buffer
open System
open System.Net
open System.Threading
open System.Net.Sockets

type Server(?rcvBufferSize:int) =
    let receiveBuffer = defaultArg rcvBufferSize 64
    let connections = 10000
    let operations = 2 // read write
    let acceptPool = new ArgsPool(4)
    let readWritePool = new ArgsPool(operations * connections)
    let bufferSize = connections * operations * receiveBuffer
    let bufferManager = new BufferManager(bufferSize,1024)
    let mutable running = false;
    let activeConnections = ref 0
    let mutable acceptHandler:(Socket Disposable-> Async<unit>) option = None
    let protect f = fun () ->
        try
            f()
        with
        | _ -> ignore()

    let disconnect (socket:Socket Disposable) =    
        match socket with
        | Disposed -> ignore()
        | Active s ->     
            Socket.disconnect socket
            let socket = socket.Dispose()
            Interlocked.Decrement(activeConnections) |> ignore

    let rec doReceive socket =
        async {
            try
                let! data = Receive readWritePool socket bufferManager
                match data with
                | None -> return None
                | Some data ->
                    if data.Length = 0
                        then 
                            try
                                disconnect socket
                                return None
                            with
                            | _ -> return None
                            
                        else 
                            match socket with
                            | Disposed -> return None
                            | Active s ->
                                Log.debugf "Received %i bytes from client %A" data.Length s.RemoteEndPoint                
                                return Some (socket,data)
            with
            | :? SocketIssue -> 
                disconnect socket
                return None
        }

    let rec doSend (socket:Socket Disposable) (data:byte[]) = 
        async {
            match socket with
            | Disposed -> ignore()
            | Active s ->
                try
                    Log.debugf "Starting send of %i bytes to client %A" data.Length s.RemoteEndPoint
                    let! sentBytes = Send readWritePool socket bufferManager data
                    match sentBytes with
                    | None -> ignore()
                    | Some sentBytes ->
                        Log.debugf "Sent %i bytes to client %A" data.Length s.RemoteEndPoint
                        if sentBytes < data.Length
                            then return! doSend socket data.[sentBytes..]
                            else ignore()
                with
                | :? SocketIssue -> disconnect socket
        }
        
    member __.Start(port:int) = 
        Log.infof "Starting server on port %i" port
        let endpoint = IPEndPoint(IPAddress.Any,port)
        let socket = new Socket(endpoint.AddressFamily,SocketType.Stream,ProtocolType.Tcp)
        socket.Bind(endpoint)
        socket.Listen(connections)
        running <- true
        let rec loop socket:Async<unit> =
            async {
                try
                    let! receiveSocket = Accept acceptPool socket
                    match receiveSocket with
                    | None -> do! loop socket
                    | Some receiveSocket ->
                        Interlocked.Increment(activeConnections) |> ignore
                        Log.debugf "Accepted connection. There are now %i active connections" !activeConnections
                        if running 
                            then 
                                match acceptHandler with
                                | None -> ignore()
                                | Some h -> h (receiveSocket) |> Async.Start
                                return! loop socket
                            else return ignore()
                with
                | :? SocketIssue -> disconnect socket
            }
        loop (Active socket) |> Async.Start

    member __.AcceptHandler
        with set(v) = acceptHandler <- Some(v)

    member __.Stop() = 
        Log.infof "Shutting down"
        running <- false

    member __.SendAsync(socket,data) =
        doSend socket data
    member __.SendSync(socket,data) =
        doSend socket data |> Async.RunSynchronously
    member __.Connections
        with get() = !activeConnections
    member __.ReceiveAsync(socket) = 
        doReceive socket
    member __.ReceiveSync(socket) =
        doReceive socket |> Async.RunSynchronously

    interface IDisposable with
        member __.Dispose() = 
            running <- false
            (acceptPool :> IDisposable).Dispose()
