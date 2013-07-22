module SampleServer

open Socket
open Server
open System
open System.Net
open System.Net.Sockets
let port = 9999

let endsWith token (s:string) = s.EndsWith(token)
let contains (token:string) (s:string) = s.IndexOf(token) > 0
let trim (s:string) = s.TrimEnd(char 0)
let decode data = System.Text.ASCIIEncoding.ASCII.GetString(data)
let encode (s:string) = System.Text.ASCIIEncoding.ASCII.GetBytes(s)

[<EntryPoint>]
let main argv = 
    printfn "Starting server on port %i" port
    let pendingMessages:Map<uint32*int,string> ref = ref Map.empty
    let messageLock = new obj()
    let getKey (s:Socket) =
        let address = (s.RemoteEndPoint :?> IPEndPoint).Address
        let port = (s.RemoteEndPoint :?> IPEndPoint).Port
        address.GetAddressBytes() |> Array.rev |> (fun a -> BitConverter.ToUInt32(a,0)),port
    
    use server = new Server()    
    let rec handleMessages socket cont =
        async {
            let! message = server.ReceiveAsync socket
            match message with
            | None -> ignore()
            | Some (socket,data) ->
                match socket with
                | Disposed -> ignore()
                | Active s ->
                    let key = getKey s
                    let msg = match !pendingMessages |> Map.tryFind key with
                              | Some msg -> sprintf "%s%s" msg (data |> decode |> trim)
                              | None -> (data |> decode |> trim)
                    match msg |> trim |> contains "\n\n" with
                    | true -> 
                        do! server.SendAsync (socket,(encode (msg.Trim())))
                        if !pendingMessages |> Map.containsKey key
                            then lock messageLock (fun () -> pendingMessages := (!pendingMessages |> Map.remove key))
                            else ignore()
                    | false -> lock messageLock (fun () -> pendingMessages := (!pendingMessages |> Map.add key msg))
                    return! cont socket
        }
    let rec handler socket = 
        async {
            return! handleMessages socket handler
        }
    server.AcceptHandler <- handler
    server.Start(port)
    printfn "Press <ENTER> to exit"
    Console.ReadLine() |> ignore    
    server.Stop()
    (server :> IDisposable).Dispose()
    0 // return an integer exit code
