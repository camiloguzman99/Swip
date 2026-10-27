using System.Text.Json;
using Swip.Shared.Engine;

// Uso: dotnet run --project tools/SimTrace -- <salida.json> [segundos] [semilla]
string output = args.Length > 0 ? args[0] : "trace.json";
double seconds = args.Length > 1 ? double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 120;
int seed = args.Length > 2 ? int.Parse(args[2]) : 4;

var world = new SwipWorld(new WorldConfig(100, 100, 66, 66, 0, 0), seed: seed); // principal del gato 100x100 y de la caja 66x66
world.Resize(900, 230);
world.PlaceBox(430);
world.AddCat("activo", 150, CatMode.Walk);
world.AddCat("fondo", 640, CatMode.Play, facingRight: false);
world.AddCat("dormido", 820, CatMode.Sleep, facingRight: false);
world.SetCatHeavy("activo", true);   // naranja: gordo
world.SetCatHeavy("dormido", true);

var log = new List<string>();
world.BehaviorStarted += (c, b) => log.Add($"{c.Id}:{b.Id}");

var frames = new List<object>();
const double dt = 1.0 / 30;
int step = 0;
for (double t = 0; t < seconds; t += dt, step++)
{
    if (Math.Abs(t - seconds * 0.5) < dt / 2) world.SetBoxOpen(true);          // a mitad: abre la caja
    if (Math.Abs(t - seconds * 0.75) < dt / 2) world.SetMode("fondo", CatMode.Walk); // la otra sesión pasa a pantalla
    world.Step(dt);
    if (step % 3 != 0) continue; // 10 instantáneas por segundo
    frames.Add(new
    {
        t = Math.Round(t, 2),
        box = new { world.Box.X, world.Box.Y, world.Box.Open, world.Box.Wobble, world.Box.Lid, world.Box.Squash },
        cats = world.Cats.Select(c => new
        {
            c.Id, c.X, c.Y, behavior = c.Current.Id, place = c.Place.ToString(),
            clip = c.Pose.Clip, frame = c.Pose.Frame, facingRight = c.Pose.FacingRight,
            ox = c.Pose.OffsetX, oy = c.Pose.OffsetY, shadow = c.Pose.ShadowDrop, behind = c.Pose.BehindBox,
        }).ToList(), // ToList: capturar el estado AHORA (Select es diferido)
        particles = world.Particles.Select(p => new { kind = p.Kind.ToString(), p.X, p.Y, p.Alpha, p.Size }).ToList(),
    });
}

File.WriteAllText(output, JsonSerializer.Serialize(new { width = world.Width, height = world.Height, frames, log }));
Console.WriteLine($"{frames.Count} instantáneas, {log.Count} comportamientos -> {output}");
foreach (var g in log.GroupBy(x => x).OrderByDescending(g => g.Count())) Console.WriteLine($"  {g.Count(),3}  {g.Key}");
