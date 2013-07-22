module Log
#nowarn "40"

type LogLevel = 
     | Debug of msg:string
     | Info of msg:string
     | Warn of msg:string
     | Error of msg:string*error:exn option
      

let private loggingAgent = MailboxProcessor<LogLevel>.Start(fun inbox ->
    let rec loop = 
        async {
            let! msg = inbox.Receive()
            match msg with
            | Debug msg          -> printfn "DEBUG: %s" msg
            | Info msg           -> printfn "INFO:  %s" msg
            | Warn msg           -> printfn "WARN:  %s" msg
            | Error (msg,None)   -> printfn "ERROR: %s" msg
            | Error (msg,Some e) -> printfn "ERROR: %s" msg
                                    printfn "       %A" e
            return! loop                
        }
    loop
)

let debugf t = Printf.ksprintf (loggingAgent.Post << Debug) t
let infof  t = Printf.ksprintf (loggingAgent.Post << Info)  t
let warnf  t = Printf.ksprintf (loggingAgent.Post << Warn)  t
let errorf t = Printf.ksprintf (fun s -> loggingAgent.Post(Error(s,None))) t
let excepf e t = Printf.ksprintf (fun s -> loggingAgent.Post(Error(s,Some e))) t