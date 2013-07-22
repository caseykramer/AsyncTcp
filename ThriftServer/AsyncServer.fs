module AsyncServer

open System
open Thrift
open Thrift.Protocol
open Thrift.Transport
open Server
open Socket
open System.IO

type TWrapperTransport(buffer:byte[],writer) as this = 
    inherit TStreamTransport(new MemoryStream(buffer),new MemoryStream())
    let mutable closed = false
    override __.IsOpen
        with get() = 
            true
    override __.Open() = 
        ignore()
    override __.Close() = 
        closed <- true
    override __.Write(buf,offset,len) = 
        writer buf.[offset..(offset+len)]
    override __.Dispose(disposing) = 
        ignore()

type TAsyncServer(processor:TProcessor,port:int,protoFactory:TProtocolFactory) =
    let server = new Server(512)
    let write socket data = 
        server.SendSync(socket,data)
    let rec doReceive socket =
        async {
            match socket with
            | Disposed -> ignore()
            | Active s ->
                let! recv = server.ReceiveAsync(socket)
                match recv with
                | None -> ignore()
                | Some (sock,data) ->
                    match sock with
                    | Active s ->
                        let wrapper = new TWrapperTransport(data,write socket)
                        let inProt = protoFactory.GetProtocol(new TFramedTransport(wrapper))
                        if processor.Process(inProt,inProt)
                            then return! doReceive socket
                            else ignore() // If TProcessor returns false, then the client disconnected
                    | Disposed -> ignore()
        }
    member __.Serve() = 
        server.AcceptHandler <- doReceive
        server.Start(port)
    member __.Stop() = 
        server.Stop()