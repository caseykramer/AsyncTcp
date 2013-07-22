module SampleClient

open System
open Client
open System.Net

let port = 9999

let encode (s:string) = System.Text.ASCIIEncoding.ASCII.GetBytes(s)
let decode (data:byte[]) = System.Text.ASCIIEncoding.ASCII.GetString(data)
let trim (s:string) = s.Trim()
let rec clientLoop (client:Client) = 
    printf "Client> "
    let text = Console.ReadLine()
    match text.ToLowerInvariant().Trim() with
    | "exit" -> ignore()
    | "" -> clientLoop client
    | _ -> 
        client.Send(text.Trim()+"\n\n" |> encode)
        let data = client.ReceiveSync() 
        match data with
        | None -> 
            printfn "Server has closed connection"
            ignore()
        | Some data ->
            printfn "SERVER: %s" <| (data |> decode |> trim)
            clientLoop client

[<EntryPoint>]
let main argv = 
    printfn "Client will attempt to connect to a server running on the local machine at port %i" port
    printfn "Press <ENTER> to connect to server"
    Console.ReadLine() |> ignore
    use client = new Client()
    client.Connect("localhost",port)    
    printfn "Connected to server on port %i" port
    printfn "Type text and press <ENTER> to send it to the server. The server will echo the text back"
    printfn "Type \"exit\" on a line by itself to quit"
    clientLoop client
    printfn "Shutting down"
    client.Disconnect()
    0 // return an integer exit code
