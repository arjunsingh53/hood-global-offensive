using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using HoodGlobalOffensive.Combat;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.Sim;
using HoodGlobalOffensive.World;

namespace HoodGlobalOffensive.Parity
{
    /// <summary>
    /// The golden-value parity suite for the Unity sim port, shared by the console harness in
    /// tools/parity and the Unity edit-mode tests in unity/Assets/Tests/EditMode. It carries no
    /// engine or test-framework references and never writes to the console itself: every result
    /// line is collected into a <see cref="ParityReport"/>.
    /// </summary>
    public static class ParityChecks
    {
        /// <summary>The accumulated, printable result of one suite run.</summary>
        public sealed class ParityReport
        {
            private readonly List<string> _lines = new();

            /// <summary>Number of failed checks; zero means full parity with the reference.</summary>
            public int Failures { get; private set; }

            /// <summary>One line per emitted result, prefixed "FAIL  " when it is a failure.</summary>
            public IReadOnlyList<string> Lines => _lines;

            internal void Note(string line) => _lines.Add(line);
            internal void Fail(string line) { Failures++; _lines.Add("FAIL  " + line); }
        }

        private static ParityReport _current = new ParityReport();

        private static void Note(string message) => _current.Note(message);
        private static void Fail(string message) => _current.Fail(message);

        // ---------------------------------------------------------------- entry points

        /// <summary>Runs every check: the in-memory goldens and both headless report replays.</summary>
        public static ParityReport RunAll(Func<string, string> loadGolden)
        {
            var report = new ParityReport();
            _current = report;
            RunSimChecksCore();
            RunHeadlessChecksCore(loadGolden);
            return report;
        }

        /// <summary>Runs the in-memory golden checks (RNG, math, weapons, maps, movement, ...).</summary>
        public static ParityReport RunSimChecks()
        {
            var report = new ParityReport();
            _current = report;
            RunSimChecksCore();
            return report;
        }

        /// <summary>
        /// Runs both CI-gate matches through the ported HeadlessRunner and compares the reports
        /// against reference captures loaded through <paramref name="loadGolden"/>.
        /// </summary>
        public static ParityReport RunHeadlessReportChecks(Func<string, string> loadGolden)
        {
            var report = new ParityReport();
            _current = report;
            RunHeadlessChecksCore(loadGolden);
            return report;
        }

        private static void RunSimChecksCore()
        {
            CheckRng();
            CheckMath();
            CheckWeapons();
            CheckFalloff();
            CheckMaps();
            CheckMover();
            CheckHitscanTrace();
            CheckNavGraph();
            CheckGrapple();
        }

        private static void RunHeadlessChecksCore(Func<string, string> loadGolden)
        {
            CheckHeadless(loadGolden);
        }

        private static void CheckRng()
        {
            CheckU64Seq(1UL, new ulong[] { 0x0000000040822041UL, 0x100041060C011441UL, 0x9B1E842F6E862629UL, 0xF554F503555D8025UL });
            CheckU64Seq(7331UL, new ulong[] { 0x00000726AB27545AUL, 0x372891B85DCB4C72UL, 0x6F3FC96E3AF9866AUL, 0xC364FA6C37ECAFE6UL });
            CheckU64Seq(20260114UL, new ulong[] { 0x004DD19E7F6C4BD8UL, 0x7F94C1B9DB65654FUL, 0x9C2D0766E5A91C45UL, 0x8C709B3F9FD7FF3DUL });
            CheckU64Seq(ulong.MaxValue, new ulong[] { 0x000000003F801FC0UL, 0x0FFFBFFE03FEEFFFUL, 0x070DC404C67D0DE0UL, 0x3217A46FEEF88FFBUL });
            CheckU64Seq(0UL, new ulong[] { 0xDC1B77AE0BF34DADUL, 0x64F0EEB9026E6076UL, 0x7B07CE91E5906136UL, 0x305F050C368DCC74UL });

            CheckFloatSeq("rng floats seed=1", 1UL, new float[] { 5.866996E-11f, 0.062503874f, 0.6059344f, 0.9583276f });
            CheckFloatSeq("rng floats seed=7331", 7331UL, new float[] { 4.2623574E-07f, 0.21546279f, 0.43456706f, 0.76325953f });
            CheckFloatSeq("rng floats seed=20260114", 20260114UL, new float[] { 0.001187421f, 0.49836358f, 0.61006206f, 0.5485932f });
            CheckFloatSeq("rng floats seed=0", 0UL, new float[] { 0.85979414f, 0.39430133f, 0.48058787f, 0.18894988f });

            var r = new Rng(12345UL);
            CheckExact("range(-5,5) #1", r.Range(-5f, 5f), -4.999993f);
            CheckExact("range(-5,5) #2", r.Range(-5f, 5f), 1.0959964f);
            CheckExact("range(-5,5) #3", r.Range(-5f, 5f), 1.3342338f);
            CheckExact("rangeInt(0,100) #1", r.RangeInt(0, 100), 80);
            CheckExact("rangeInt(0,100) #2", r.RangeInt(0, 100), 74);
            CheckExact("rangeInt(0,100) #3", r.RangeInt(0, 100), 39);
            CheckExact("rangeInt(0,100) #4", r.RangeInt(0, 100), 22);
            CheckExact("chance(0.25)", r.Chance(0.25f), true);
            CheckExact("rangeInt empty guards", new Rng(7UL).RangeInt(5, 5), 5);
        }

        private static void CheckMath()
        {
            var d = U.DirFromAngles(37.5f, -12.25f);
            CheckNear("dir(37.5,-12.25).X", d.X, -0.5949006f);
            CheckNear("dir(37.5,-12.25).Y", d.Y, -0.21217766f);
            CheckNear("dir(37.5,-12.25).Z", d.Z, -0.77528954f);

            var rv = U.RightFromYaw(123.456f);
            CheckNear("right(123.456).X", rv.X, -0.5512964f);
            CheckExact("right(123.456).Y", rv.Y, 0f);
            CheckNear("right(123.456).Z", rv.Z, -0.83430946f);

            CheckExact("angledelta(350,10)", U.AngleDelta(350f, 10f), 20f);
            CheckExact("angledelta(10,350)", U.AngleDelta(10f, 350f), -20f);
            CheckNear("yawfromdir(dir)", U.YawFromDirection(d), 37.5f);
            CheckNear("pitchfromdir(dir)", U.PitchFromDirection(d), -12.249999f);

            var flat = U.FlatNormalized(new Vector3(3f, 9f, 4f));
            CheckExact("flatnorm(3,9,4).X", flat.X, 0.6f);
            CheckExact("flatnorm(3,9,4).Y", flat.Y, 0f);
            CheckExact("flatnorm(3,9,4).Z", flat.Z, 0.8f);

            var nz = U.NormalizeOrZero(new Vector3(0f, 0f, 0f));
            CheckExact("normzero(0,0,0).X", nz.X, 0f);
            CheckExact("normzero(0,0,0).Y", nz.Y, 0f);
            CheckExact("normzero(0,0,0).Z", nz.Z, 0f);

            CheckExact("movetowards(1,10,2.5)", U.MoveTowards(1f, 10f, 2.5f), 3.5f);
            CheckNear("approach(0,100,6,0.5)", U.Approach(0f, 100f, 6f, 0.5f), 95.02129f);
        }

        private static void CheckWeapons()
        {
            var rifle = new Weapon(WeaponRegistry.Rifle, 1);
            CheckExact("rifle idle spread", rifle.SpreadFor(0f, true, false, 0f), 0.28f);
            CheckExact("rifle stand-air spread", rifle.SpreadFor(0f, false, false, 0f), 15.28f);
            CheckExact("rifle move-air spread", rifle.SpreadFor(400f, false, false, 0f), 20.78f);
            CheckExact("rifle crouch-ground spread", rifle.SpreadFor(0f, true, true, 0f), 0.1736f);
            CheckExact("rifle ads-idle spread", rifle.SpreadFor(0f, true, false, 1f), 0.126f);
            CheckExact("rifle ads-move-air spread", rifle.SpreadFor(400f, false, false, 1f), 20.626f);
            rifle.Bloom = 3f;
            CheckExact("rifle bloom3 spread", rifle.SpreadFor(0f, true, false, 0f), 3.28f);
            rifle.Tick(0f, 0.25f);
            CheckExact("rifle bloom after 0.25s", rifle.Bloom, 1.85f);

            var shotgun = new Weapon(WeaponRegistry.Shotgun, 1);
            CheckExact("shotgun idle spread", shotgun.SpreadFor(0f, true, false, 0f), 4.6f);

            var smg = new Weapon(WeaponRegistry.Smg, 1);
            smg.Bloom = 6f;
            CheckExact("smg bloom6 air-move ads0.5 spread", smg.SpreadFor(400f, false, false, 0.5f), 19.32f);

            var recoilSmg = new Weapon(WeaponRegistry.Smg, 1);
            var rng = new Rng(7331UL);
            Vector2[] expectedRecoil =
            {
                new(-0.11199991f, 0.45499998f),
                new(-0.08285723f, 0.50399995f),
                new(-0.023451166f, 0.553f),
                new(0.11204325f, 0.602f),
                new(-0.24591744f, 0.65099996f),
                new(-0.15060739f, 0.7f),
            };
            for (int i = 0; i < expectedRecoil.Length; i++)
            {
                Vector2 got = recoilSmg.NextRecoil(ref rng);
                CheckExact($"smg recoil shot{i} yaw", got.X, expectedRecoil[i].X);
                CheckExact($"smg recoil shot{i} pitch", got.Y, expectedRecoil[i].Y);
            }

            var revolver = new Weapon(WeaponRegistry.Revolver, 1);
            var rng2 = new Rng(20260114UL);
            Vector2 revolverShot = revolver.NextRecoil(ref rng2);
            CheckExact("revolver recoil yaw", revolverShot.X, -0.19952504f);
            CheckExact("revolver recoil pitch", revolverShot.Y, 2.9899998f);

            var reloadSmg = new Weapon(WeaponRegistry.Smg, 1, reserveOverride: 40, magOverride: 0);
            bool began = reloadSmg.BeginReload(10f);
            reloadSmg.Tick(10.5f, 0.5f);
            CheckExact("smg reload began", began, true);
            CheckExact("smg mid-reload mag", reloadSmg.Mag, 0);
            CheckExact("smg mid-reload reserve", reloadSmg.Reserve, 40);
            CheckExact("smg mid-reload reloading", reloadSmg.Reloading, true);
            CheckExact("smg reload end time", reloadSmg.ReloadEndTime, 11f);
            reloadSmg.Tick(11f, 0.5f);
            CheckExact("smg done mag", reloadSmg.Mag, 40);
            CheckExact("smg done reserve", reloadSmg.Reserve, 0);
            CheckExact("smg done reloading", reloadSmg.Reloading, false);

            var reloadRifle = new Weapon(WeaponRegistry.Rifle, 1, magOverride: 10);
            bool rifleReload = reloadRifle.BeginReload(5f);
            CheckExact("rifle reload begin", rifleReload, true);
            CheckExact("rifle reload end time", reloadRifle.ReloadEndTime, 6.5f);
            CheckExact("rifle reload started empty", reloadRifle.ReloadStartedEmpty, false);

            var db = new Weapon(WeaponRegistry.DoubleBarrel, 1);
            db.ReadyTime = 0.4f;
            CheckExact("db canfire@0.39", db.CanFire(0.39f), false);
            CheckExact("db canfire@0.4", db.CanFire(0.4f), true);

            var knife = new Weapon(WeaponRegistry.Knife, 4);
            CheckExact("knife canfire@0", knife.CanFire(0f), true);
            CheckExact("knife uses ammo", knife.Def.UsesAmmo, false);
        }

        private static void CheckFalloff()
        {
            float[] shotgunDistances = { 0f, 400f, 799f, 800f, 1200f, 1800f, 2800f, 5000f };
            float[] shotgunExpected = { 17f, 17f, 17f, 17f, 13.94f, 9.35f, 1.7000004f, 1.7000004f };
            for (int i = 0; i < shotgunDistances.Length; i++)
            {
                CheckNear($"shotgun falloff @{shotgunDistances[i]}", Hitscan.BaseDamageAtRange(WeaponRegistry.Shotgun, shotgunDistances[i]), shotgunExpected[i]);
            }

            float[] dbDistances = { 0f, 300f, 600f, 900f, 1200f, 2000f };
            float[] dbExpected = { 90f, 90f, 90f, 54.000004f, 17.999998f, 17.999998f };
            for (int i = 0; i < dbDistances.Length; i++)
            {
                CheckNear($"doublebarrel falloff @{dbDistances[i]}", Hitscan.BaseDamageAtRange(WeaponRegistry.DoubleBarrel, dbDistances[i]), dbExpected[i]);
            }
        }

        private static void CheckMaps()
        {
            var industrial = new GameMap("Industrial");
            CheckExact("industrial brush count", industrial.Brushes.Length, 55);
            CheckExact("industrial spawn count", industrial.Spawns.Length, 12);

            (float X, float Z, float Yaw)[] expectedSpawns =
            {
                (-1157.7891f, -666.4581f, 90f),
                (-581.78906f, -730.4581f, 90f),
                (698.21094f, -250.4581f, 270f),
                (-1189.7891f, -26.4581f, 90f),
                (666.21094f, -90.4581f, 270f),
                (-965.78906f, 293.5419f, 90f),
                (-133.7891f, 389.5419f, 180f),
                (746.21094f, 389.5419f, 270f),
                (-197.7891f, 837.5419f, 0f),
                (826.21094f, 805.5419f, 180f),
                (-965.78906f, 805.5419f, 0f),
                (218.2109f, 261.5419f, 180f),
            };
            for (int i = 0; i < expectedSpawns.Length; i++)
            {
                SpawnPoint s = industrial.Spawns[i];
                CheckNear($"industrial spawn{i} X", s.Position.X, expectedSpawns[i].X);
                CheckExact($"industrial spawn{i} Y", s.Position.Y, 0f);
                CheckNear($"industrial spawn{i} Z", s.Position.Z, expectedSpawns[i].Z);
                CheckExact($"industrial spawn{i} yaw", s.Yaw, expectedSpawns[i].Yaw);
            }

            bool hit = industrial.RayHit(new Vector3(0f, 64f, 0f), new Vector3(1f, 0f, 0f), 5000f,
                out float d, out Vector3 p, out Vector3 n, out int bi);
            CheckExact("industrial rayhit", hit, true);
            CheckExact("industrial rayhit d", d, 0f);
            CheckNear("industrial rayhit p.X", p.X, 0f);
            CheckNear("industrial rayhit p.Y", p.Y, 64f);
            CheckExact("industrial rayhit n.X", n.X, 0f);
            CheckExact("industrial rayhit n.Y", n.Y, 0f);
            CheckExact("industrial rayhit n.Z", n.Z, 0f);
            CheckExact("industrial rayhit brush", bi, 28);

            bool floor = industrial.FindFloorSurface(new Vector3(0f, 100f, 0f), 200f, 400f, out float fy, out SurfaceKind surf);
            CheckExact("industrial floor", floor, true);
            CheckNear("industrial floor y", fy, 132.92601f);
            CheckExact("industrial floor surface", surf, SurfaceKind.Crate);

            CheckExact("industrial blocked(0,64,0)", industrial.BoxBlocked(new Aabb(new Vector3(-10f, 54f, -10f), new Vector3(10f, 74f, 10f))), true);
            CheckExact("industrial segblocker(0,64,0 -> 900,64,0)", industrial.SegmentBlocker(new Vector3(0f, 64f, 0f), new Vector3(900f, 64f, 0f)), 28);
            CheckExact("industrial brushat(0,50,0)", industrial.BrushAt(new Vector3(0f, 50f, 0f)), 28);
            CheckExact("industrial brushat(0,300,0)", industrial.BrushAt(new Vector3(0f, 300f, 0f)), -1);

            var arena = new GameMap("Arena");
            CheckExact("arena brush count", arena.Brushes.Length, 42);
            CheckExact("arena spawn count", arena.Spawns.Length, 11);

            var rng = new Rng(7331UL);
            var avoid = new List<Vector3> { new(0f, 0f, 0f), new(500f, 0f, 500f) };
            SpawnPoint pick = arena.PickSpawn(ref rng, avoid, 800f);
            CheckNear("arena pick X", pick.Position.X, 970f);
            CheckExact("arena pick Y", pick.Position.Y, 0f);
            CheckNear("arena pick Z", pick.Position.Z, -880f);
            CheckExact("arena pick yaw", pick.Yaw, 135f);

            CheckExact("arena los(0,64,0 -> 800,64,800)", arena.LineOfSight(new Vector3(0f, 64f, 0f), new Vector3(800f, 64f, 800f)), false);
            CheckExact("arena floor(0,300,0)", arena.FindFloor(new Vector3(0f, 300f, 0f), 400f, 800f, out float afy), true);
            CheckExact("arena floor y", afy, 64f);

            bool dais = arena.RayHit(new Vector3(0f, 200f, 0f), new Vector3(0f, -1f, 0f), 1000f,
                out float ad, out Vector3 ap, out _, out int abi);
            CheckExact("arena dais rayhit", dais, true);
            CheckNear("arena dais rayhit d", ad, 136f);
            CheckExact("arena dais rayhit brush", abi, 5);

            CheckText("map normalize unknown", MapRegistry.Normalize("nope"), "Industrial");
            CheckText("map normalize case", MapRegistry.Normalize("arena"), "Arena");
            _ = ap;
        }

        private static void CheckMover()
        {
            var map = new GameMap("Arena");
            var actor = new ProbeActor { Position = new Vector3(-600f, 0f, -600f) };
            actor.OnGround = true;

            const float dt = 0.125f;
            (int Step, float PX, float PY, float PZ, float VX, float VY, float VZ, float Crouch, bool Ground)[] expected =
            {
                (8, -297.67587f, 0.03125f, -418.74255f, 187.49998f, 0f, 324.75955f, 0f, true),
                (32, -998.47455f, 17.84375f, -245.60063f, -480.67105f, -195f, -146.1204f, 0f, false),
                (48, -956.0299f, 0.03125f, -919.9413f, 0f, 0f, 1.8358784E-05f, 1f, true),
                (63, -974.2949f, 30.34375f, -400.7353f, -146.1203f, 242.5f, 480.67114f, 0f, false),
            };

            int check = 0;
            for (int step = 0; step < 64 && check < expected.Length; step++)
            {
                float angle = step * 7.5f * MathF.PI / 180f;
                var wish = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
                bool jump = step % 12 == 3;
                bool crouch = step >= 40 && step <= 48;
                CharacterMover.Move(map, actor, wish, 500f, jump, crouch, dt);

                if (step != expected[check].Step) continue;

                (int Step, float PX, float PY, float PZ, float VX, float VY, float VZ, float Crouch, bool Ground) e = expected[check];
                check++;
                CheckNear($"mover step{e.Step} p.X", actor.Position.X, e.PX);
                CheckNear($"mover step{e.Step} p.Y", actor.Position.Y, e.PY);
                CheckNear($"mover step{e.Step} p.Z", actor.Position.Z, e.PZ);
                CheckNear($"mover step{e.Step} v.X", actor.Velocity.X, e.VX);
                CheckNear($"mover step{e.Step} v.Y", actor.Velocity.Y, e.VY);
                CheckNear($"mover step{e.Step} v.Z", actor.Velocity.Z, e.VZ);
                CheckNear($"mover step{e.Step} crouch", actor.CrouchAmount, e.Crouch);
                CheckExact($"mover step{e.Step} ground", actor.OnGround, e.Ground);
            }
        }

        private static void CheckHitscanTrace()
        {
            var map = new GameMap("Arena");
            var shooter = new ProbeActor { Position = new Vector3(520f, 0f, -600f) };
            var victim = new ProbeActor { Position = new Vector3(520f, 0f, -180f) };
            var actors = new List<Actor> { shooter, victim };

            var rng = new Rng(4242UL);
            ShotResult rifle = Hitscan.Trace(map, actors, shooter,
                new Vector3(520f, 40f, -600f), new Vector3(0f, 0f, 1f), WeaponRegistry.Rifle, ref rng);
            CheckExact("trace rifle hitActor", rifle.HitActor, true);
            CheckExact("trace rifle group", rifle.Group, HitGroup.Chest);
            CheckNear("trace rifle dmg", rifle.Damage, 30f);
            CheckNear("trace rifle pt.X", rifle.Point.X, 520f);
            CheckNear("trace rifle pt.Y", rifle.Point.Y, 40f);
            CheckNear("trace rifle pt.Z", rifle.Point.Z, -192f);
            CheckExact("trace rifle through", rifle.ThroughCover, false);
            CheckExact("trace rifle brush", rifle.BrushIndex, 2);
            CheckExact("trace rifle killed", rifle.Killed, false);

            var rng2 = new Rng(9UL);
            ShotResult shotgun = Hitscan.Trace(map, actors, shooter,
                new Vector3(520f, 70f, -600f), new Vector3(0f, 0f, 1f), WeaponRegistry.Shotgun, ref rng2);
            CheckExact("trace shotgun hitActor", shotgun.HitActor, true);
            CheckExact("trace shotgun group", shotgun.Group, HitGroup.Head);
            CheckNear("trace shotgun dmg", shotgun.Damage, 17f);
            CheckNear("trace shotgun pt.X", shotgun.Point.X, 520f);
            CheckNear("trace shotgun pt.Y", shotgun.Point.Y, 70f);
            CheckNear("trace shotgun pt.Z", shotgun.Point.Z, -187f);

            var rng3 = new Rng(5UL);
            ShotResult upward = Hitscan.Trace(map, actors, shooter,
                new Vector3(520f, 40f, -600f), new Vector3(0f, 1f, 0f), WeaponRegistry.Rifle, ref rng3);
            CheckExact("trace upward hitActor", upward.HitActor, false);
            CheckExact("trace upward hitWorld", upward.HitWorld, false);
            CheckNear("trace upward dmg", upward.Damage, 0f);
            CheckNear("trace upward pt.X", upward.Point.X, 520f);
            CheckNear("trace upward pt.Y", upward.Point.Y, 7040f);
            CheckNear("trace upward pt.Z", upward.Point.Z, -600f);
            CheckNear("trace upward normal.Y", upward.Normal.Y, -1f);
            CheckExact("trace upward brush", upward.BrushIndex, -1);
        }

        private static void CheckNavGraph()
        {
            var arena = new GameMap("Arena");
            var nav = NavGraph.Build(arena);
            CheckExact("navg arena nodes", nav.Nodes.Length, 904);

            (int I, float X, float Y, float Z, int Nb)[] samples =
            {
                (0, -1220f, 0f, -1220f, 3),
                (10, -420f, 0f, -1220f, 5),
                (100, -660f, 0f, -980f, 7),
                (400, 540f, 0f, -180f, 7),
            };
            foreach ((int i, float x, float y, float z, int nb) in samples)
            {
                NavGraph.Node node = nav.Nodes[i];
                CheckNear($"navg arena node{i} X", node.Position.X, x);
                CheckNear($"navg arena node{i} Y", node.Position.Y, y);
                CheckNear($"navg arena node{i} Z", node.Position.Z, z);
                CheckExact($"navg arena node{i} nb", node.Neighbours.Length, nb);
            }
            CheckNear("navg arena node903 X", nav.Nodes[903].Position.X, 1180f);
            CheckExact("navg arena node903 nb", nav.Nodes[903].Neighbours.Length, 3);

            CheckExact("navg arena nearest(-600,0,-600)", nav.NearestNode(new Vector3(-600f, 0f, -600f)), 238);

            int[] arenaPath =
            {
                97, 98, 99, 128, 154, 180, 209, 241, 273, 304,
                334, 362, 393, 424, 455, 486, 518, 549, 578, 608,
                638, 639, 668, 700, 732, 758, 783,
            };
            CheckPath("navg arena path", nav.FindPath(new Vector3(-900f, 0f, -900f), new Vector3(900f, 0f, 900f)), arenaPath);

            var industrial = new GameMap("Industrial");
            var navI = NavGraph.Build(industrial);
            CheckExact("navg industrial nodes", navI.Nodes.Length, 785);

            int[] industrialPath =
            {
                70, 101, 132, 150, 170, 171, 172, 173, 193, 226,
                246, 267, 287, 307, 328, 350, 380, 412, 446, 475,
                509, 543, 575, 609, 641, 642, 643, 644, 645, 646,
                680,
            };
            CheckPath("navg industrial path", navI.FindPath(new Vector3(-1000f, 0f, -900f), new Vector3(900f, 0f, 900f)), industrialPath);
        }

        private static void CheckPath(string label, List<int> actual, int[] expected)
        {
            if (actual.Count != expected.Length)
            {
                Fail($"{label}: got {actual.Count} ids, want {expected.Length}");
                return;
            }

            for (int i = 0; i < expected.Length; i++)
            {
                if (actual[i] != expected[i])
                {
                    Fail($"{label}: id #{i} got {actual[i]}, want {expected[i]}");
                    return;
                }
            }
        }

        private static void CheckGrapple()
        {
            var map = new GameMap("Arena");
            var actor = new ProbeActor { Position = new Vector3(-600f, 0f, -600f) };
            actor.Yaw = U.YawFromDirection(new Vector3(-1f, 0f, -1f));
            actor.Pitch = 30f;

            var hook = new GrappleHook();
            bool attached = GrappleSystem.TryAttach(map, actor, hook);
            CheckExact("grapple attach", attached, true);
            CheckExact("grapple anchored", hook.Attached, true);
            CheckNear("grapple anchor.X", hook.Anchor.X, -740f);
            CheckNear("grapple anchor.Y", hook.Anchor.Y, 178.30952f);
            CheckNear("grapple anchor.Z", hook.Anchor.Z, -740f);
            CheckNear("grapple rope", hook.RopeLength, 228.61905f);

            (int Step, float PX, float PY, float PZ, float VX, float VY, float VZ, bool Ground)[] expected =
            {
                (8, -625.98254f, 56f, -625.98254f, -71.2336f, 23.32381f, -71.2336f, true),
                (32, -694.59204f, 0.009557724f, -694.59204f, -18.163185f, 45.719986f, -18.163185f, true),
                (63, -694.59204f, 0.009557724f, -694.59204f, -18.163185f, 45.719986f, -18.163185f, true),
            };

            int check = 0;
            for (int step = 0; step < 64 && check < expected.Length; step++)
            {
                CharacterMover.Move(map, actor, Vector3.Zero, 0f, false, false, 0.125f);
                if (hook.Attached) GrappleSystem.Tick(map, actor, hook, 0.125f);
                if (step != expected[check].Step) continue;

                (int Step, float PX, float PY, float PZ, float VX, float VY, float VZ, bool Ground) e = expected[check];
                check++;
                CheckExact($"grapple step{e.Step} attached", hook.Attached, true);
                CheckNear($"grapple step{e.Step} p.X", actor.Position.X, e.PX);
                CheckNear($"grapple step{e.Step} p.Y", actor.Position.Y, e.PY);
                CheckNear($"grapple step{e.Step} p.Z", actor.Position.Z, e.PZ);
                CheckNear($"grapple step{e.Step} v.X", actor.Velocity.X, e.VX);
                CheckNear($"grapple step{e.Step} v.Y", actor.Velocity.Y, e.VY);
                CheckNear($"grapple step{e.Step} v.Z", actor.Velocity.Z, e.VZ);
                CheckExact($"grapple step{e.Step} ground", actor.OnGround, e.Ground);
            }
        }

        // ---------------------------------------------------------------- headless

        /// <summary>
        /// Runs the ported HeadlessRunner for both CI gates and byte-compares the
        /// deterministic report against captures of the raylib reference.
        /// </summary>
        private static void CheckHeadless(Func<string, string> loadGolden)
        {
            CheckHeadlessRun("headless-60s-15bots-hard-seed7331.txt", 60f, 15, BotSkill.Hard, 7331UL, loadGolden);
            CheckHeadlessRun("headless-300s-9bots-normal-seed20260114.txt", 300f, 9, BotSkill.Normal, 20260114UL, loadGolden);
        }

        private static void CheckHeadlessRun(string goldenName, float seconds, int bots, BotSkill skill, ulong seed,
            Func<string, string> loadGolden)
        {
            string label = $"headless {seconds:0}s {bots} bots {skill} seed {seed}";

            string expected;
            try
            {
                expected = NormalizeReport(loadGolden(goldenName));
            }
            catch (Exception ex)
            {
                Fail($"{label} golden capture unavailable ({goldenName}): {ex.Message}");
                return;
            }

            var writer = new StringWriter();
            TextWriter stdout = Console.Out;
            int exit;
            try
            {
                Console.SetOut(writer);
                exit = HeadlessRunner.Run(seconds, bots, skill, seed, verbose: false);
            }
            finally
            {
                Console.SetOut(stdout);
            }

            CheckExact($"{label} exit code", exit, 0);

            string actual = NormalizeReport(writer.ToString());
            if (actual == expected)
            {
                Note($"ok    {label} report matches reference capture");
                return;
            }

            string[] actualLines = actual.Split('\n');
            string[] expectedLines = expected.Split('\n');
            for (int i = 0; i < Math.Max(actualLines.Length, expectedLines.Length); i++)
            {
                string a = i < actualLines.Length ? actualLines[i] : "<missing>";
                string e = i < expectedLines.Length ? expectedLines[i] : "<missing>";
                if (a != e)
                {
                    Fail($"{label} report line {i + 1}:");
                    Note($"        got  {a}");
                    Note($"        want {e}");
                    break;
                }
            }
        }

        /// <summary>
        /// Keeps the report body only and drops the wall-clock line, the one
        /// intentionally non-deterministic value in the runner's output.
        /// </summary>
        private static string NormalizeReport(string text)
        {
            text = text.Replace("\r\n", "\n");
            var lines = new List<string>();
            bool body = false;
            foreach (string line in text.Split('\n'))
            {
                if (!body)
                {
                    if (!line.StartsWith("====", StringComparison.Ordinal)) continue;
                    body = true;
                }

                if (line.Contains(" wall clock")) continue;
                lines.Add(line);
            }

            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            return string.Join("\n", lines) + "\n";
        }

        private sealed class ProbeActor : Actor
        {
        }

        private static void CheckU64Seq(ulong seed, ulong[] expected)
        {
            var rng = new Rng(seed);
            for (int i = 0; i < expected.Length; i++)
            {
                ulong actual = rng.NextU64();
                if (actual != expected[i])
                {
                    Fail($"rng u64 seed={seed} #{i + 1}: got {actual:X16}, want {expected[i]:X16}");
                }
            }
        }

        private static void CheckFloatSeq(string label, ulong seed, float[] expected)
        {
            var rng = new Rng(seed);
            for (int i = 0; i < expected.Length; i++)
            {
                float actual = rng.NextFloat();
                if (actual != expected[i])
                {
                    Fail($"{label} #{i + 1}: got {actual:R}, want {expected[i]:R}");
                }
            }
        }

        private static void CheckExact<T>(string label, T actual, T expected)
            where T : struct
        {
            if (!actual.Equals(expected))
            {
                Fail($"{label}: got {actual}, want {expected}");
            }
        }

        private static void CheckText(string label, string actual, string expected)
        {
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                Fail($"{label}: got {actual}, want {expected}");
            }
        }

        private static void CheckNear(string label, float actual, float expected)
        {
            if (MathF.Abs(actual - expected) > 1e-5f)
            {
                Fail($"{label}: got {actual:R}, want {expected:R}");
            }
        }
    }
}
