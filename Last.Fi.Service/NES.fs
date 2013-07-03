namespace LastFi.Modules

open LastFi.Common
open System
open System.Collections.Generic
open System.Text

module NES =
    
    type Button =
     | A =       0b00000001
     | B =       0b00000010
     | SELECT =  0b00000100
     | START =   0b00001000
     | UP =      0b00010000
     | DOWN =    0b00100000
     | LEFT =    0b01000000
     | RIGHT =   0b10000000                               

     type ButtonData = Map<Button,int64>
     type CustomEvent = (ButtonData * Button list -> bool)   
    
    let (|ButtonDown|_|) b data =
        match fst data |> Map.tryFind b with
        | Some(v) -> Some(TimeSpan.FromTicks(DateTime.Now.Ticks - v).TotalMilliseconds)
        | None -> None

    let (|ButtonSequence|_|) s data =
        let s = s |> List.rev // b will be for example [left;left;up;down]. Reverse it so its in the same order as our history 
        let h = snd data
        let rec aux b h =
            match b, h with
            | b::bs, h::hs when b = h -> aux bs hs 
            | b::bs, h::hs when b <> h -> false
            | b::bs, [] -> false
            | [], _ -> true
            | _ -> false
        if aux s h then Some(())
        else None

    type private NESMessage = 
        | Read of AsyncReplyChannel<Map<Button,int64>*Button list>
        | AddCustomEvent of (CustomEvent * Event<unit>)
        | RemoveCustomEvent of Event<unit>
    
    /// NES Controller is basically a 8 bit parallel to serial shift register
    /// the module from parallax has two sockets and two data lines where both sockets
    /// share the clock and latch lines - this is currently only dealing with one pad
    type NES(CLK:GPIOPins, LATCH:GPIOPins, DATA1:GPIOPins, DATA2:GPIOPins, pollDelay:int option) as nes = 
        let buttonDown = new Event<_>()
        let buttonUp = new Event<_>()               
        let fireEvents = pollDelay.IsSome
        let buttonTimeout = 150.0  // 150ms repeat delay 

        let agent = new MailboxProcessor<NESMessage>(fun inbox -> 
            let updateState currentState currentHistory = 
                let (|TimeOut|JustPressed|Pressed|LetGo|NotPressed|) (button:Button,pressed:bool) =
                    match currentState |> Map.containsKey button, pressed with
                    | false, true -> JustPressed
                    | true, false -> LetGo
                    | true, true when TimeSpan.FromTicks(DateTime.Now.Ticks-currentState.[button]).TotalMilliseconds > buttonTimeout -> TimeOut
                    | true, true  -> Pressed
                    | false,false -> NotPressed

                let newState = nes.ReadControllers()

                let buttonFold (currentState,currentHistory) button =
                    let button = enum<Button> button
                    match (button,newState &&& (int button) > 0) with
                    | NotPressed 
                    | Pressed -> (currentState,currentHistory) // do nothing here ( ?? )
                    | JustPressed -> if fireEvents then buttonDown.Trigger(button)
                                     (currentState |> Map.add button DateTime.Now.Ticks,button :: currentHistory)                    
                    | TimeOut -> // in a timeout we pretend the button is being pressed repeatedly (think keyboard repeat delay)                                 
                                 if fireEvents then buttonDown.Trigger(button)
                                 (currentState,currentHistory)
                    | LetGo -> if fireEvents then buttonUp.Trigger(button,TimeSpan.FromTicks(DateTime.Now.Ticks - currentState.[button]).TotalMilliseconds)
                               (currentState |> Map.remove button,currentHistory)
                    
                let e = Enum.GetValues(typeof<Button>)

                let (newState,newHistory) =
                    ((currentState,currentHistory),[0..e.Length - 1] |> List.map(e.GetValue >> unbox)) ||> List.fold buttonFold
                (newState,newHistory |> Seq.truncate 20 |> Seq.toList ) // TODO: Performance with truncate not optimal here
                 
            let rec loop (currentState,currentHistory,customEvents) = 
               match pollDelay with
               | Some(poll) -> async {
                    let! msg = inbox.TryReceive(poll)
                    let (newState,newHistory,newEvents) = 
                        match msg with
                        | Some(Read(reply)) -> 
                            // return updated current state and do not fire standard events 
                            let (newState,newHistory) = updateState currentState  currentHistory
                            reply.Reply (newState,newHistory)
                            (newState,newHistory,customEvents)
                        | Some(AddCustomEvent(pred,event)) -> (currentState,currentHistory,(pred,event) :: customEvents)
                        | Some(RemoveCustomEvent(event)) -> (currentState,currentHistory,List.filter(snd>>(=)event) customEvents)
                        | None -> 
                           // update state internally and fire normal events
                           let (newState,newHistory) = updateState currentState currentHistory
                           // trigger custom events where applicable. simple for now, 
                           // if one gets found then trigger, cull history and stop looking for more
                           let newHistory =
                               customEvents 
                               |> List.tryFind( fun (pred,_) -> pred (newState,newHistory) )
                               |> function
                                  | Some(_,event) -> event.Trigger(); []
                                  | None -> newHistory
                           (newState,newHistory,customEvents)
                    return! loop (newState,newHistory,newEvents) }
                | None -> async { 
                    // update state internally and fire normal events
                    let (newState,newHistory) = updateState currentState currentHistory
                    // trigger custom events where applicable
                    for (pred,event) in customEvents do if pred (newState,newHistory) then event.Trigger()
                    return! loop (newState,newHistory,customEvents) }
          
            loop (Map.empty,[],[]))

        [<CLIEvent>]
        member __.ButtonDown = buttonDown.Publish

        [<CLIEvent>]
        member __.ButtonUp = buttonUp.Publish

        member __.StartAsyncCycle() = agent.Start()

        member __.Initialize() = 
            fsel DATA1 false 
            fsel CLK true; fsel LATCH true     // clock and latch are outputs
            write LATCH false                  // pull latch low initially 
            write CLK false

        member private __.ReadControllers() =
            write LATCH true; wait 10
            write LATCH false; wait 1
            (0b0,[0..7])
            ||> List.fold( fun acc i ->  
                let next = read DATA1
                // pulse clock to shift next bit onto the data line
                write CLK true; wait 1
                write CLK false; wait 1
                // we only care about LOW values, if its LOW then the button is pressed
                if next = false then (acc ||| (0b1 <<< i)) // shift 1 along i bits and AND it into the accumulator
                else acc)
        
        member __.GetControllerData() = agent.PostAndReply(fun reply -> Read(reply))

        member __.AddCustomEvent(pred,event) = agent.Post(AddCustomEvent(pred,event))
        member __.RemoveCustomEvent(event) = agent.Post(RemoveCustomEvent(event))


        interface System.IDisposable
            with member __.Dispose() = (agent :> System.IDisposable).Dispose()