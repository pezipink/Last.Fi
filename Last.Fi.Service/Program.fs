module LastFi.Core

open System
open ServiceStack.ServiceHost
open ServiceStack.WebHost.Endpoints

open System.Diagnostics
open LastFi.Common
open LastFi.Player
open LastFi.Modules

open Last.Fi.Common

let player = LastFi.Player.Player()

type LastFiService() =
    interface IService<Current> with
        member this.Execute (req:Current) = 
            let getCurrent() = 
                match player.CurrentTrack() with
                | SomethingElse(_)
                | PlaylistEmpty -> { Artist = "Awaiting data.."; Track = "Awaiting data.."; Album = "Awaiting data.."; Volume = "Awaiting data.."; Playing = MPC.isPlaying().ToString() } :> obj
                | TrackPlaying(track) -> { Artist = track.Artist; Track = track.Title; Album = track.AlbumTitle; Volume = MPC.getVolume().ToString(); Playing = MPC.isPlaying().ToString() } :> obj
            match req.Op with
            | "Next" -> MPC.next() :> _
            | "Info" -> 
                match player.CurrentTrack() with
                | SomethingElse(_)
                | PlaylistEmpty -> { Blurb = "No data available"; Image = "" } :> obj
                | TrackPlaying(track) -> 
                    let (blurb,image) = player.GetArtistInfo(track.Artist)                    
                    { Blurb = blurb; Image = image } :> obj 
            | "Current" ->
                getCurrent()
            | "VolumeUp" -> 
                player.VolumeUp()
                getCurrent()
            | "VolumeDown" -> 
                player.VolumeDown()
                getCurrent()
            | "PlayPause" -> 
                player.Toggle()
                getCurrent()
            | _ -> "" :> obj
               
 
//Define the Web Services AppHost
type AppHost =
    inherit AppHostHttpListenerBase
    new() = { inherit AppHostHttpListenerBase("Last.Fi Services", typeof<LastFiService>.Assembly) }
    override this.Configure container =
        base.Routes
            .Add<Current>("/lastfi/")  
            .Add<Current>("/lastfi/{Op}")  |> ignore
 
let getIpAddress() =
    System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList 
    |> Seq.find(fun ip -> ip.AddressFamily = System.Net.Sockets.AddressFamily.InterNetwork)
    
//Run it!
[<EntryPoint>]
let main args =   
    // initialize the Pi's IO pin interface
    bcm2835_init() |> ignore
    
    // create the LCD and NES interfaces
    let lcd = LCD(GPIOPins.Pin_11,GPIOPins.Pin_12,GPIOPins.Pin_13,GPIOPins.Pin_15,GPIOPins.Pin_16,GPIOPins.Pin_18,GPIOPins.Pin_22)    
    let nes = new NES.NES(GPIOPins.Pin_SDA,GPIOPins.Pin_SCL,GPIOPins.Pin_7,GPIOPins.GPIO_None,Some(10))
    
    lcd.Initialize() // sets pins and sends wakeup sequence to prepare LCD for use
    nes.Initialize() // sets pins and makes ready for data polling   

    // display the IP address on the LCD whilst spinning up the service stack services
    printfn "IP Adress : %s " <| getIpAddress().ToString()
    lcd.ScrollText(getIpAddress().ToString(),".....")
    let host = if args.Length = 0 then "http://*:8080/" else args.[0]
    printfn "listening on %s ..." host
    let appHost = new AppHost()
    appHost.Init()
    appHost.Start host    

    // initialize connection to the last.fm api 
    if player.Initialize() = false then failwith "Fatal exception occured when trying to inialize player. Program terminated"

    // add an event handler to the player that will update the LCD when tracks change
    // and load more tracks from Last.FM when the playlist is empty
    player.SomethingChanged.Add( 
        function
        | TrackPlaying(track) -> 
            printfn "now playing %s : %s" track.Artist track.Title 
            lcd.ScrollText(track.Artist,track.Title)
        | SomethingElse(msg) ->
            printfn "%s" msg
            lcd.ScrollText(msg,"")
        | PlaylistEmpty -> 
            printfn "Ran out of tracks to play!" 
            lcd.ScrollText("Loading tracks","from Last.FM..")
            lcd.Command(LCDCommands.Clear)
            player.QueueNewTracks())

    lcd.ScrollText(getIpAddress().ToString(),"Press Start")

    let started = ref false
    let shutdown = ref false

    let printVolume() = 
        let vol = MPC.getVolume()
        let blocks = int ((16.0 / 100.0) * float vol)
        let empty = 16 - blocks
        let visual = String('#',blocks) + String('-',empty)
        lcd.TemporaryText(sprintf "Volume : %i%%" vol, visual)

    nes.ButtonDown.Add(function
                        | NES.Button.RIGHT -> player.Next()
                        | NES.Button.START ->  
                            if !started  then player.Toggle()
                            else started := true; player.QueueNewTracks()
                        
                        | NES.Button.DOWN -> player.VolumeDown();printVolume()
                        | (NES.Button.UP) -> player.VolumeUp();printVolume()
                        | _ -> ())
    
    nes.StartAsyncCycle()
        
    let startSelectEvent = Event<unit>()
    startSelectEvent.Publish.Add(fun _ -> printfn "start select!!")

    let konamiCodeEvent = Event<unit>()
    konamiCodeEvent.Publish.Add(fun _ -> printfn "Konami Code!!!"
                                         let aux = async {
                                            for i = 1 to 5 do
                                                // this will cause the message to flash 5 times (exciting!!)
                                                lcd.TemporaryText("Konami Code!","") 
                                                do! Async.Sleep(750) 
                                                lcd.TemporaryText("","")
                                                do! Async.Sleep(750) 
                                            return () }
                                         Async.Start aux )


    nes.AddCustomEvent((fun data ->
                        match data with
                        | NES.ButtonDown NES.Button.START length1 
                          & NES.ButtonDown NES.Button.SELECT length2 when length2 > 3000.0 && length1 > 3000.0 -> true
                        | _ -> false), startSelectEvent)

    nes.AddCustomEvent((function
                        | NES.ButtonSequence
                           [NES.Button.UP;NES.Button.UP;NES.Button.DOWN;NES.Button.DOWN;
                            NES.Button.LEFT;NES.Button.RIGHT;NES.Button.LEFT;NES.Button.RIGHT;
                            NES.Button.B;NES.Button.A] () -> true
                        | _ -> false),konamiCodeEvent)

    
    while(!shutdown=false) do System.Threading.Thread.Sleep(1000)
    MPC.stop()
    lcd.ScrollText("Goodbye :(","See you soon!!")
    System.Threading.Thread.Sleep(3000)
    0 