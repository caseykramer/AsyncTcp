module Program

open System
open Thrift
open AsyncServer
open Thrift.Test


[<EntryPoint>]
let main args = 
    let port = 10050
    let server = 
        { new PingServer.Iface with
            member __.Ping() = "Hi"}
    let server = new AsyncServer.TAsyncServer(new PingServer.Processor(server),port,Protocol.TBinaryProtocol.Factory())
    printfn "Starting ping server on port %i" port
    server.Serve()
    printfn "Press <ENTER> to exit"
    Console.ReadLine() |> ignore
    server.Stop()
    0