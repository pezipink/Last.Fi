module MPC
open System
// wrappers around the MPC->MPD command line interface 
open System.Diagnostics
    
let mpc args = 
    let pi = ProcessStartInfo("mpc",args)
    let proc = Process.Start(pi)
    proc.WaitForExit()
    proc.Close()

let mpcResult args = 
     let pi = ProcessStartInfo("mpc",args)        
     pi.UseShellExecute <- false
     pi.RedirectStandardOutput <- true
     pi.CreateNoWindow <- true
     let proc = Process.Start(pi)               
     proc.WaitForExit()     
     proc.StandardOutput.ReadToEnd().Trim() 

let mpcResultAsync args = async { return mpcResult args } 

let consumeOn() = mpc "consume on"
let play() = mpc "play"
let clear() = mpc "clear"
let stop() = mpc "stop"
let next() = mpc "next"
let toggle() = mpc "toggle"
let volumeUp() = mpc "volume +5"
let volumeDown() = mpc "volume -5"

let queue location = mpc (sprintf "add %s" location)

let getVolume() = 
    let current = mpcResult "volume"
    Int32.Parse(current.Replace("%","").Replace("volume:","").Trim())

let isPlaying() = 
    let status = mpcResult "status"
    status.Contains("[playing]")

let getCurrent() = mpcResult "current"