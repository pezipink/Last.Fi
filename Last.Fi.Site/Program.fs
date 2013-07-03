[<ReflectedDefinition>]
module Program

open FunScript
open System.Net
open Last.Fi.Common

type j = TypeScript.Api<"../Typings/jquery.d.ts">
type lib = TypeScript.Api<"../Typings/lib.d.ts">
let jQuery (command:string) = j.jQuery.Invoke(command)

let main()  = 
    let status = jQuery("<label>")   
    status.appendTo(jQuery("#status")) |> ignore
    let volume = jQuery("<label>")   
    volume.appendTo(jQuery("#volume")) |> ignore
    let artist = jQuery("<label>")    
    artist.appendTo(jQuery("#artist")) |> ignore
    let track = jQuery("<label>")    
    track.appendTo(jQuery("#track")) |> ignore
    let album = jQuery("<label>")    
    album.appendTo(jQuery("#album")) |> ignore
    let image = jQuery("<img>")    
    image.appendTo(jQuery("#image")) |> ignore 
    let bio = jQuery("<label>") 
    bio.appendTo(jQuery("#blurb")) |> ignore
    
    let playpause = jQuery("<input>")
    playpause.attr("type", "button") |> ignore
    playpause.appendTo(jQuery("#buttons")) |> ignore 
    playpause.attr("value", "Play / Pause") |> ignore 
    playpause.click(fun _ -> j.jQuery.getJSON("http://lastfi:8080/lastfi?Op=PlayPause&callback=?")) |> ignore
    
    let next = jQuery("<input>") 
    next.attr("type", "button") |> ignore
    next.attr("value", "Next Track") |> ignore 
    next.appendTo(jQuery("#buttons")) |> ignore 
    next.click(fun _ -> j.jQuery.getJSON("http://lastfi:8080/lastfi?Op=Next&callback=?")) |> ignore

    let increase = jQuery("<input>") 
    increase.attr("type", "button") |> ignore
    increase.attr("value", "Increase Volume") |> ignore 
    increase.appendTo(jQuery("#buttons")) |> ignore 
    increase.click(fun _ -> j.jQuery.getJSON("http://lastfi:8080/lastfi?Op=VolumeUp&callback=?")) |> ignore

    let decrease = jQuery("<input>") 
    decrease.attr("type", "button") |> ignore
    decrease.attr("value", "Decrease Volume") |> ignore 
    decrease.appendTo(jQuery("#buttons")) |> ignore 
    decrease.click(fun _ -> j.jQuery.getJSON("http://lastfi:8080/lastfi?Op=VolumeDown&callback=?")) |> ignore
    

    lib.setInterval(
        fun _ -> j.jQuery.getJSON("http://lastfi:8080/lastfi?Op=Current&callback=?",null,
                    fun (data:CurrentResponse) -> 
                        let artist' = "Artist : " + data.Artist 
                        let track' = "Track : " + data.Track
                        let album' = "Album : " + data.Album     
                        let volume' = "Volume : " + data.Volume + "%"
                        if data.Playing = "True" then status.html("Currently Playing :")  |> ignore
                        else status.html("Paused.")  |> ignore
                        volume.html(volume') |> ignore
                        if artist' = artist.html().ToString() && track' = track.html().ToString() && album' = album.html().ToString() then ()
                        else
                            artist.html(artist') |> ignore
                            track.html(track')   |> ignore
                            album.html(album')   |> ignore
                            // get artist info
                            j.jQuery.getJSON("http://lastfi:8080/lastfi?Op=Info&callback=?",null,
                                fun(data:InfoResponse) -> 
                                    bio.html(data.Blurb) |> ignore
                                    image.attr("src",data.Image) |> ignore
                                    image.attr("class","displayed")) |> ignore )
        ,2000.0 )

// ------------------------------------------------------------------
let components = 
  FunScript.Data.Components.DataProviders @ 
  FunScript.Interop.Components.all
do Runtime.Run(components=components, directory="Web")
