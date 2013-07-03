module LastFi.Common

open System.Runtime.InteropServices

type GPIODirection =
    | In = 0
    | Out = 1

type GPIOPins =
    | GPIO_None = 4294967295u
    | Pin_SDA = 2u
    | Pin_SCL = 3u
    | Pin_7   = 4u
    | Pin_11  = 17u
    | Pin_12  = 18u
    | Pin_13  = 27u
    | Pin_15  = 22u
    | Pin_16  = 23u
    | Pin_18  = 24u
    | Pin_22  = 25u

[<DllImport("libbcm2835.so", EntryPoint = "bcm2835_init")>]
extern bool bcm2835_init()

[<DllImport("libbcm2835.so", EntryPoint = "bcm2835_gpio_fsel")>]
extern void bcm2835_gpio_fsel(GPIOPins pin, bool mode_out);

[<DllImport("libbcm2835.so", EntryPoint = "bcm2835_gpio_write")>]
extern void bcm2835_gpio_write(GPIOPins pin, bool value);

[<DllImport("libbcm2835.so", EntryPoint = "bcm2835_gpio_lev")>]
extern bool bcm2835_gpio_lev(GPIOPins pin);

let fsel pin value = bcm2835_gpio_fsel(pin,value)                        
let write pin value = bcm2835_gpio_write(pin,value)            
let read pin = bcm2835_gpio_lev(pin)
let wait (ms:int) = System.Threading.Thread.Sleep(ms)