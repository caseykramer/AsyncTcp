module Socket
#nowarn "40"
open System
open Buffer
open System.Net.Sockets
 
type Disposable<'a when 'a :> IDisposable> =
    | Active of 'a
    | Disposed with
        member this.Dispose() = 
            match this with
            | Active d ->
                d.Dispose()
                Disposed
            | Disposed ->
                Disposed
        member this.Item 
            with get() =
                match this with
                | Active d -> Some d
                | Disposed _ -> None

module Disposable =
    let dispose (d:'a Disposable) = d.Dispose()
    let isDisposed (d:'a Disposable) = match d with
                                       | Active _ -> false
                                       | _ -> true
    let disposable (d:#IDisposable) = Active d
    let disposed (d:#IDisposable) = Disposed
    let map (f:'a -> 'b) (d:'a Disposable) = 
        match d with
        | Active d -> Active (f d)
        | Disposed -> Disposed
    let iter (f:'a -> unit) (d:'a Disposable) =
        match d with
        | Active d -> f d
        | Disposed -> ignore()

type B = BufferManager

module Observable = 
    let once (f:'a -> unit) observable = 
        let rec disposable = observable |> Observable.subscribe (fun a -> f(a);disposable.Dispose())
        disposable
 
exception SocketIssue of SocketError with
    override this.ToString() =
        string this.Data0

type ArgsPool(size) = 
    let mutable pool = [for i in 0..size do yield new Args()]
    static let locker = new obj()
    member __.Push(arg:Args) =
        arg.AcceptSocket <- null
        lock locker (fun () -> pool <- arg::pool)
    member __.Pop() = 
        let item = pool.Head
        lock locker (fun () -> pool <- pool.Tail)
        item
    interface IDisposable with
        member __.Dispose() = 
            pool |> List.iter (fun a -> a.Dispose())
 
/// Wraps the Socket.xxxAsync logic into F# async logic.
let asyncDo (pool:ArgsPool) (op: Args -> bool) (prepare: Args -> unit)
    (select: Args -> 'T) =    
    Async.FromContinuations <| fun (ok, error, _) ->
        let args = pool.Pop()
        prepare args
        let k (disp:Lazy<IDisposable>) (args: Args) =
            try
                match args.SocketError with
                | System.Net.Sockets.SocketError.Success ->
                    let result = select args
                    disp.Force().Dispose()
                    pool.Push(args)
                    ok result
                | e ->
                    disp.Force().Dispose()
                    pool.Push(args)
                    error (SocketIssue e)
            with
            | _ as e -> Log.warnf "Error while executing continuation: %A" e
        let rec disp = args.Completed |> Observable.subscribe (k (lazy(disp)))
        if not (op args) then
            k (lazy(disp)) args
    
/// Prepares the arguments by setting the buffer.
let inline setBuffer (buf: B) (args: Args) =
 buf.SetBuffer(args) |> ignore // what to do here....

let inline setSend (buf:B) (data) (args: Args) =
    buf.SetSendBuffer(args,data) |> ignore // calculate what didn't get sent after send completes

let inline disconnect (socket:Socket Disposable) = 
    match socket with
    | Disposed -> ignore()
    | Active s ->
        try
            s.Shutdown(SocketShutdown.Both)
        with _ -> ignore()
        s.Close()
        

let Accept pool (socket: Socket Disposable) =
    match socket with
    | Disposed -> async { return None }
    | Active s ->
        asyncDo pool s.AcceptAsync ignore (fun a -> Some (Active a.AcceptSocket))
 
let Receive pool (socket: Socket Disposable) (buf: B) =
    match socket with
    | Disposed -> async { return None }
    | Active s ->
        asyncDo pool s.ReceiveAsync (setBuffer buf)
            (fun a ->
                if a.BytesTransferred = 0
                    then 
                        disconnect socket
                        None
                    else 
                        if a.Buffer = null
                            then None
                            else let result = a.Buffer.[a.Offset..(a.Offset+(a.BytesTransferred - 1))]
                                 buf.FreeBuffer(a,1024)
                                 Some result)
 
let Send pool (socket: Socket Disposable) (buf: B) data =
    match socket with
    | Disposed -> async { return None }
    | Active s ->
        asyncDo pool s.SendAsync (setSend buf data) 
            (fun a -> 
                buf.FreeBuffer(a,data.Length)
                Some a.BytesTransferred)

let Connect pool (socket: Socket Disposable) endPoint = 
    match socket with
    | Disposed -> async { return None }
    | Active s -> 
        asyncDo pool s.ConnectAsync (fun a -> a.RemoteEndPoint <- endPoint) (fun a -> Some (Active a.ConnectSocket))

let Disconnect pool (socket: Socket Disposable) =
    match socket with
    | Disposed -> async { return() }
    | Active s ->
        asyncDo pool s.DisconnectAsync ignore ignore

