#load "SurfaceTypes.fsx"

open System
open System.Text.Json

JsonSerializer.Deserialize<uint32[]>(Console.In.ReadToEnd())
|> Array.map SurfaceTypes.collisionFlags
|> JsonSerializer.Serialize
|> Console.WriteLine
