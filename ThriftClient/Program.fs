// Learn more about F# at http://fsharp.net
// See the 'F# Tutorial' project for more help.

open System
open Thrift
open Thrift.Test
open System.Net.Sockets

[<EntryPoint>]
let main argv = 
    let protoFactory = Protocol.TBinaryProtocol.Factory()
    use transport = new Transport.TSocket("localhost",10050)
    let client = new PingServer.Client(protoFactory.GetProtocol(new Transport.TFramedTransport(transport)))
    printfn "Starting client, connecting to port 10050"
    transport.Open()
    printfn "Press <ENTER> to send Ping to the server"
    printfn "Type \"enter\" on a line by itself to exit"        
    let rec loop() = 
        printf "Ping_Client:> "    
        let msg = Console.ReadLine()
        match (msg.Trim()) with
        | "exit" -> ignore()
        | "" ->
            let response = client.Ping();
            printfn "Server says: %s" response
            loop()
        | _ -> 
            printfn "What? I don't understand"
            loop()
    loop()
    printfn "Goodbye"
    0 // return an integer exit code
