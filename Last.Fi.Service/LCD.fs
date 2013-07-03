
namespace LastFi.Modules

open System
open LastFi.Common
open System.Text

type LCDCommands =
    | AllLow =          0b00000000
    | Clear =           0b00000001
    | Home =            0b00000010
    | FourBit =         0b00100000
    | TwoLine =         0b00001100
    | DisplayOn =       0b00001100
    | CursorOn =        0b00000001
    | AutoIncCursor =   0b00000110
    | Line2 =           0xC0

type private LCDMessages =
    | ScrollText of string * string
    | TemporaryText of string * string

type LCD(E:GPIOPins, RW:GPIOPins, RS:GPIOPins, DB4:GPIOPins, DB5:GPIOPins, DB6:GPIOPins, DB7:GPIOPins) as lcd  = 
    let agent = MailboxProcessor<LCDMessages>.Start ( fun inbox -> 
        printfn "starting agent"
        let writeLines one two =    
            lcd.WriteText(one,true)
            lcd.Command(LCDCommands.Line2)
            lcd.WriteText(two,false)            
        // the LCD is always scrolling what it is currently displaying if possible,
        // and it does this every 1.5 seconds.  
        let rec loop (current1,current2,offset1,offset2,alwaysDraw) = async {
            let! msg = inbox.TryReceive(1500)
            match msg with 
            | Some(ScrollText(line1,line2)) -> 
                writeLines line1 line2
                return! loop (line1,line2,0,0,false)
            | Some(TemporaryText(line1,line2)) -> 
                // in temporary text mode, show the new text then restore the old text as per message receive timeout
                // don't worry about scrolling - text in this mode should always fit the screen
                writeLines line1 line2
                return! loop (current1,current2,offset1,offset2,true)
            | None ->
                let spill1 = current1.Length - 16
                let spill2 = current2.Length - 16
                if spill1 <= 0 && spill2 <= 0 then 
                    if alwaysDraw then writeLines current1 current2
                    return! loop (current1,current2,0,0,false)
                else
                let offset1 = if offset1 < spill1 then offset1 + 2 else 0
                let offset2 = if offset2 < spill2 then offset2 + 2 else 0
                writeLines 
                    (current1.Substring(offset1,min 16 (current1.Length - offset1))) 
                    (current2.Substring(offset2,min 16 (current2.Length - offset2))) 
                return! loop (current1,current2,offset1,offset2,false) }
        loop ("","",0,0,true))

    member __.Pulse() = // toggles enable 
        write E true; wait 1
        write E false; wait 1
    member __.WriteNibble(value) = // write the lower four bits to the data pins and pulses
        write DB7 (value >>> 3 &&& 0x1 = 0x1)
        write DB6 (value >>> 2 &&& 0x1 = 0x1)
        write DB5 (value >>> 1 &&& 0x1 = 0x1)
        write DB4 (value &&& 0x1 = 0x1)
        lcd.Pulse()
        wait 1
    member __.WriteByte(value) =
        lcd.WriteNibble(value >>> 4) // write high nibble first
        lcd.WriteNibble(value)
    member __.Command = int >> lcd.WriteByte
    member __.Initialize() = // I am only using the (annoyingly fiddly) 4 bit mode
        // assume 1000ms or so has passed since program start up
        // make sure pins are set to output
        printfn "initializing LCD"
        fsel E   true; fsel RW  true
        fsel RS  true; fsel DB4 true
        fsel DB5 true; fsel DB6 true
        fsel DB7 true
        // zero them all out
        lcd.Command LCDCommands.AllLow
        // to start with we are only writing special wakeup nibbles
        lcd.WriteNibble(0x3); wait 5 // as per spec, first call has a 5ms wait
        lcd.WriteNibble(0x3); wait 1
        lcd.WriteNibble(0x3); wait 1
        // now set into 4 bit mode and send 8 bits in 2 nibbles from now on
        lcd.WriteNibble(0x2)
        lcd.Command(LCDCommands.FourBit ||| LCDCommands.TwoLine)
        lcd.Command(LCDCommands.DisplayOn )  // switch it on
        lcd.Command(LCDCommands.AutoIncCursor)
        lcd.Command(LCDCommands.Clear)
    member __.WriteText(text:string,clear) = 
        if clear then lcd.Command(LCDCommands.Clear)
        write RS true; wait 1
        Encoding.ASCII.GetBytes(text) |> Seq.iter(int >> lcd.WriteByte)
        write RS false; wait 1
    
    member __.ScrollText(line1,line2) = agent.Post(ScrollText(line1,line2))
    member __.TemporaryText(line1,line2) = agent.Post(TemporaryText(line1,line2)) // undercover agents!!
