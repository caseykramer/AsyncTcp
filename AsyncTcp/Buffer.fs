module Buffer

open System
open System.Net
open System.Net.Sockets
type Args = SocketAsyncEventArgs
type Agent<'a> = MailboxProcessor<'a>

type private BufferOperation = 
             | Allocate of Args*AsyncReplyChannel<bool>
             | Send of Args*byte[]*AsyncReplyChannel<byte[]>
             | Free of size:int
             | CurrentBuffer of AsyncReplyChannel<int>

type BufferManager(totalSize,window) =
    let buffer:byte[] = Array.zeroCreate(totalSize)
    let bufferAgent = MailboxProcessor<BufferOperation>.Start(fun inbox -> 
        let rec loop offset = 
            async {
                let! msg = inbox.Receive()
                match msg with
                | Allocate (saea,reply) ->
                    if offset + window > totalSize 
                        then reply.Reply(false)
                        else saea.SetBuffer(buffer,offset,window)
                             reply.Reply(true)
                    return! loop (offset + window)
                | Send ((saea:SocketAsyncEventArgs),data,reply) ->
                    let length = Math.Min(data.Length,window)
                    Array.Copy(data,0,buffer,offset,length)
                    saea.SetBuffer(buffer,offset,length)
                    reply.Reply(data.[length..])
                    return! loop (offset+window)
                | Free size ->
                    let newOffset = Math.Min(0,offset - window)
                    Array.fill buffer offset window 0uy 
                    return! loop (newOffset)
                | CurrentBuffer reply ->
                    reply.Reply(offset)
                    return! loop offset
            }
        loop 0
    )

    member __.SetBuffer(args) = 
        bufferAgent.PostAndReply(fun r -> Allocate(args,r))
    member __.SetSendBuffer(args,data) = 
        bufferAgent.PostAndReply(fun r -> Send(args,data,r))
    member __.FreeBuffer(args:Args,size:int) =
        bufferAgent.Post(Free(size))
        args.SetBuffer(null,0,0)
        