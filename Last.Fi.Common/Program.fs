module Last.Fi.Common

[<CLIMutable>]
type CurrentResponse = { Artist : string; Track : string; Album : string; Volume : string; Playing : string }

[<CLIMutable>]
type InfoResponse = { Blurb: string; Image : string }
