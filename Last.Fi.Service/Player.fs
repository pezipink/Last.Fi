namespace LastFi.Player

open System
open System.Collections.Generic
open System.Diagnostics
open LastFi.Api

[<CLIMutable>]
type Current = { Op: string; }

[<CLIMutable>]
type Next = { Name: string; }

type PlayerResponse =
    | TrackPlaying of Track
    | PlaylistEmpty
    | SomethingElse of string

type PlayerMessage =
    | Toggle                                                                    // toggles play / pause 
    | QueueNewTracks                                                            // queues and plays the next 5 tracks from last.fm
    | GetArtistInfo of AsyncReplyChannel<string*string> * string                // retrieves artist info 
    | GetCurrentTrack of AsyncReplyChannel<PlayerResponse>                      // retrieves currently playing track info
    | Iniitalize of AsyncReplyChannel<bool> * string * string * string * string // username, password, api key, api secret


type PlayerState = { station : Radio; session : Session; trackMap : Map<string,Track> }

type Player() =
    let somethingChanged = new Event<_>()        

    let agent = MailboxProcessor<PlayerMessage>.Start(fun inbox -> 
        let rec aux state = async {
            let! msg = inbox.Receive()
            match msg with
            | Iniitalize(reply,user,pass,api,sekret) ->
                let result =
                    try
                        printfn "Authenticating session for user %s" user
                        let session = Session.Authenticate(user,pass,api,sekret)
                        printfn "Success! Conencting to user's radio station"
                        let station = Radio.Tune(sprintf "lastfm://user/%s/mix" user,session)
                        reply.Reply(true)
                        Some({station=station;session=session;trackMap=Map.empty})
                    with 
                    | _ -> 
                        reply.Reply(false)
                        None
                return! aux result
            | Toggle -> 
                if state.IsNone then printfn "Player is not initalized" else 
                MPC.toggle()
            | QueueNewTracks ->
                if state.IsNone then printfn "Player is not initalized" else 
                let tracks = state.Value.station.GetTracks()
                MPC.stop()
                MPC.clear()
                let trackMap =
                    [for track in tracks do 
                        printfn "adding track %s %s %s" track.StreamPath track.Artist track.Title 
                        MPC.queue track.StreamPath
                        yield (track.StreamPath,track)] |> Map.ofList
                MPC.consumeOn()
                MPC.play()
                return! aux (Some({ state.Value with trackMap = trackMap}))
            | GetArtistInfo(reply,name) ->
                if state.IsNone then printfn "Player is not initalized" else
                let artist = Artist.GetInfo(name,state.Value.session)
                reply.Reply(artist.Bio,artist.Image)
                return! aux state
            | GetCurrentTrack(reply) -> 
                if state.IsNone then printfn "Player is not initalized" 
                let current = MPC.getCurrent()
                let result = 
                    if String.IsNullOrEmpty current then PlaylistEmpty
                    elif Map.containsKey current state.Value.trackMap then TrackPlaying(state.Value.trackMap.[current])
                    else SomethingElse("Track data was not found in map")
                reply.Reply(result)
                return! aux state } 
        aux None)
        
    [<CLIEvent>]
    member __.SomethingChanged = somethingChanged.Publish

    member __.Initialize() = 
        // load the last fm details from file and try to connect
        let (username,password,api,sekret) =
            System.IO.File.ReadAllLines "/home/pi/creds.dat"
            |> fun d-> (d.[0],d.[1],d.[2],d.[3])
        let result  = agent.PostAndReply(fun reply -> Iniitalize(reply,username,password,api,sekret))
        if result then 
             // start async loop listening for changes to MPC
            let rec loop() = async {
                let! out = MPC.mpcResultAsync  "idle playlist player"
                let! current = agent.PostAndAsyncReply(fun reply -> GetCurrentTrack(reply))
                somethingChanged.Trigger current    
                return! loop() }
            //todo: cancellcation token. If you call initalize twice you will get two loops running
            loop() |> Async.Start
            true
        else
            printfn "failed to initialize player - check credentials file and connection" 
            false 
           
    member __.GetArtistInfo(name:string) = agent.PostAndReply(fun reply -> GetArtistInfo(reply,name))
    member __.CurrentTrack() = agent.PostAndReply(fun reply -> GetCurrentTrack(reply))
    member __.QueueNewTracks() = agent.Post(QueueNewTracks)
    member __.VolumeUp() = MPC.volumeUp()
    member __.VolumeDown() = MPC.volumeDown()
    member __.Toggle() = MPC.toggle()
    member __.Next() = MPC.next()
        
        