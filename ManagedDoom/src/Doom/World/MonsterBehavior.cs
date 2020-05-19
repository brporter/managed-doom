﻿using System;

namespace ManagedDoom
{
    public sealed class MonsterBehavior
    {
        private World world;


        public MonsterBehavior(World world)
        {
            this.world = world;
        }


        public void Fall(Mobj actor)
        {
            // Actor is on ground, it can be walked over.
            actor.Flags &= ~MobjFlags.Solid;
        }


        private bool LookForPlayers(Mobj actor, bool allAround)
        {
            var count = 0;

            var stop = (actor.LastLook - 1) & 3;

            for (; ; actor.LastLook = (actor.LastLook + 1) & 3)
            {
                if (!world.Players[actor.LastLook].InGame)
                {
                    continue;
                }

                if (count++ == 2 || actor.LastLook == stop)
                {
                    // Done looking.
                    return false;
                }

                var player = world.Players[actor.LastLook];

                if (player.Health <= 0)
                {
                    // Player is dead.
                    continue;
                }

                if (!world.VisibilityCheck.CheckSight(actor, player.Mobj))
                {
                    // Out of sight.
                    continue;
                }

                if (!allAround)
                {
                    var angle = Geometry.PointToAngle(
                        actor.X, actor.Y,
                        player.Mobj.X, player.Mobj.Y) - actor.Angle;

                    if (angle > Angle.Ang90 && angle < Angle.Ang270)
                    {
                        var dist = Geometry.AproxDistance(
                            player.Mobj.X - actor.X,
                            player.Mobj.Y - actor.Y);

                        // If real close, react anyway.
                        if (dist > World.MELEERANGE)
                        {
                            // Behind back.
                            continue;
                        }
                    }
                }

                actor.Target = player.Mobj;

                return true;
            }
        }


        public void Look(Mobj actor)
        {
            // Any shot will wake up.
            actor.Threshold = 0;

            var target = actor.Subsector.Sector.SoundTarget;

            if (target != null && (target.Flags & MobjFlags.Shootable) != 0)
            {
                actor.Target = target;

                if ((actor.Flags & MobjFlags.Ambush) != 0)
                {
                    if (world.VisibilityCheck.CheckSight(actor, actor.Target))
                    {
                        goto seeYou;
                    }
                }
                else
                {
                    goto seeYou;
                }
            }

            if (!LookForPlayers(actor, false))
            {
                return;
            }

        // Go into chase state.
        seeYou:
            if (actor.Info.SeeSound != 0)
            {
                int sound;

                switch (actor.Info.SeeSound)
                {
                    case Sfx.POSIT1:
                    case Sfx.POSIT2:
                    case Sfx.POSIT3:
                        sound = (int)Sfx.POSIT1 + world.Random.Next() % 3;
                        break;

                    case Sfx.BGSIT1:
                    case Sfx.BGSIT2:
                        sound = (int)Sfx.BGSIT1 + world.Random.Next() % 2;
                        break;

                    default:
                        sound = (int)actor.Info.SeeSound;
                        break;
                }

                if (actor.Type == MobjType.Spider || actor.Type == MobjType.Cyborg)
                {
                    // Full volume.
                    world.StartSound(null, (Sfx)sound);
                }
                else
                {
                    world.StartSound(actor, (Sfx)sound);
                }
            }

            actor.SetState(actor.Info.SeeState);
        }


        private static readonly Fixed[] xSpeed =
        {
            new Fixed(Fixed.FracUnit),
            new Fixed(47000),
            new Fixed(0),
            new Fixed(-47000),
            new Fixed(-Fixed.FracUnit),
            new Fixed(-47000),
            new Fixed(0),
            new Fixed(47000)
        };

        private static readonly Fixed[] ySpeed =
        {
            new Fixed(0),
            new Fixed(47000),
            new Fixed(Fixed.FracUnit),
            new Fixed(47000),
            new Fixed(0),
            new Fixed(-47000),
            new Fixed(-Fixed.FracUnit),
            new Fixed(-47000)
        };

        private bool Move(Mobj actor)
        {
            if (actor.MoveDir == Direction.None)
            {
                return false;
            }

            if ((int)actor.MoveDir >= 8)
            {
                throw new Exception("Weird actor->movedir!");
            }

            var tryX = actor.X + actor.Info.Speed * xSpeed[(int)actor.MoveDir];
            var tryY = actor.Y + actor.Info.Speed * ySpeed[(int)actor.MoveDir];

            var tm = world.ThingMovement;

            var tryOk = tm.TryMove(actor, tryX, tryY);

            if (!tryOk)
            {
                // Open any specials.
                if ((actor.Flags & MobjFlags.Float) != 0 && tm.FloatOk)
                {
                    // Must adjust height.
                    if (actor.Z < tm.CurrentFloorZ)
                    {
                        actor.Z += ThingMovement.FloatSpeed;
                    }
                    else
                    {
                        actor.Z -= ThingMovement.FloatSpeed;
                    }

                    actor.Flags |= MobjFlags.InFloat;

                    return true;
                }

                if (tm.crossedSpecialCount == 0)
                {
                    return false;
                }

                actor.MoveDir = Direction.None;
                var good = false;
                while (tm.crossedSpecialCount-- > 0)
                {
                    var line = tm.crossedSpecials[tm.crossedSpecialCount];
                    // If the special is not a door that can be opened,
                    // return false.
                    if (world.MapInteraction.UseSpecialLine(actor, line, 0))
                    {
                        good = true;
                    }
                }
                return good;
            }
            else
            {
                actor.Flags &= ~MobjFlags.InFloat;
            }

            if ((actor.Flags & MobjFlags.Float) == 0)
            {
                actor.Z = actor.FloorZ;
            }

            return true;
        }

        private bool TryWalk(Mobj actor)
        {
            if (!Move(actor))
            {
                return false;
            }

            actor.MoveCount = world.Random.Next() & 15;

            return true;
        }


        private static readonly Direction[] opposite =
        {
            Direction.west, Direction.Southwest, Direction.South, Direction.Southeast,
            Direction.East, Direction.Northeast, Direction.North, Direction.Northwest,
            Direction.None
        };

        private static readonly Direction[] diags =
        {
            Direction.Northwest, Direction.Northeast, Direction.Southwest, Direction.Southeast
        };

        private readonly Direction[] choices = new Direction[3];

        private void NewChaseDir(Mobj actor)
        {
            if (actor.Target == null)
            {
                throw new Exception("Called with no target.");
            }

            var oldDir = actor.MoveDir;
            var turnAround = opposite[(int)oldDir];

            var deltaX = actor.Target.X - actor.X;
            var deltaY = actor.Target.Y - actor.Y;

            if (deltaX > Fixed.FromInt(10))
            {
                choices[1] = Direction.East;
            }
            else if (deltaX < Fixed.FromInt(-10))
            {
                choices[1] = Direction.west;
            }
            else
            {
                choices[1] = Direction.None;
            }

            if (deltaY < Fixed.FromInt(-10))
            {
                choices[2] = Direction.South;
            }
            else if (deltaY > Fixed.FromInt(10))
            {
                choices[2] = Direction.North;
            }
            else
            {
                choices[2] = Direction.None;
            }

            // Try direct route.
            if (choices[1] != Direction.None && choices[2] != Direction.None)
            {
                var a = (deltaY < Fixed.Zero) ? 1 : 0;
                var b = (deltaX > Fixed.Zero) ? 1 : 0;
                actor.MoveDir = diags[(a << 1) + b];

                if (actor.MoveDir != turnAround && TryWalk(actor))
                {
                    return;
                }
            }

            // Try other directions.
            if (world.Random.Next() > 200 || Fixed.Abs(deltaY) > Fixed.Abs(deltaX))
            {
                var temp = choices[1];
                choices[1] = choices[2];
                choices[2] = temp;
            }

            if (choices[1] == turnAround)
            {
                choices[1] = Direction.None;
            }

            if (choices[2] == turnAround)
            {
                choices[2] = Direction.None;
            }

            if (choices[1] != Direction.None)
            {
                actor.MoveDir = choices[1];

                if (TryWalk(actor))
                {
                    // Either moved forward or attacked.
                    return;
                }
            }

            if (choices[2] != Direction.None)
            {
                actor.MoveDir = choices[2];

                if (TryWalk(actor))
                {
                    return;
                }
            }

            // There is no direct path to the player, so pick another direction.
            if (oldDir != Direction.None)
            {
                actor.MoveDir = oldDir;

                if (TryWalk(actor))
                {
                    return;
                }
            }

            // Randomly determine direction of search.
            if ((world.Random.Next() & 1) != 0)
            {
                for (var dir = (int)Direction.East; dir <= (int)Direction.Southeast; dir++)
                {
                    if ((Direction)dir != turnAround)
                    {
                        actor.MoveDir = (Direction)dir;

                        if (TryWalk(actor))
                        {
                            return;
                        }
                    }
                }
            }
            else
            {
                for (var dir = (int)Direction.Southeast; dir != ((int)Direction.East - 1); dir--)
                {
                    if ((Direction)dir != turnAround)
                    {
                        actor.MoveDir = (Direction)dir;

                        if (TryWalk(actor))
                        {
                            return;
                        }
                    }
                }
            }

            if (turnAround != Direction.None)
            {
                actor.MoveDir = turnAround;

                if (TryWalk(actor))
                {
                    return;
                }
            }

            // Can not move.
            actor.MoveDir = Direction.None;
        }


        private bool CheckMeleeRange(Mobj actor)
        {
            if (actor.Target == null)
            {
                return false;
            }

            var target = actor.Target;

            var dist = Geometry.AproxDistance(target.X - actor.X, target.Y - actor.Y);

            if (dist >= World.MELEERANGE - Fixed.FromInt(20) + target.Info.Radius)
            {
                return false;
            }

            if (!world.VisibilityCheck.CheckSight(actor, actor.Target))
            {
                return false;
            }

            return true;
        }


        private bool CheckMissileRange(Mobj actor)
        {
            if (!world.VisibilityCheck.CheckSight(actor, actor.Target))
            {
                return false;
            }

            if ((actor.Flags & MobjFlags.JustHit) != 0)
            {
                // The target just hit the enemy, so fight back!
                actor.Flags &= ~MobjFlags.JustHit;

                return true;
            }

            if (actor.ReactionTime > 0)
            {
                // Do not attack yet
                return false;
            }

            // OPTIMIZE:
            //     Get this from a global checksight.
            var dist = Geometry.AproxDistance(
                actor.X - actor.Target.X,
                actor.Y - actor.Target.Y) - Fixed.FromInt(64);

            if (actor.Info.MeleeState == 0)
            {
                // No melee attack, so fire more.
                dist -= Fixed.FromInt(128);
            }

            var attackDist = dist.Data >> 16;

            if (actor.Type == MobjType.Vile)
            {
                if (attackDist > 14 * 64)
                {
                    // Too far away.
                    return false;
                }
            }

            if (actor.Type == MobjType.Undead)
            {
                if (attackDist < 196)
                {
                    // Close for fist attack.
                    return false;
                }

                attackDist >>= 1;
            }


            if (actor.Type == MobjType.Cyborg ||
                actor.Type == MobjType.Spider ||
                actor.Type == MobjType.Skull)
            {
                attackDist >>= 1;
            }

            if (attackDist > 200)
            {
                attackDist = 200;
            }

            if (actor.Type == MobjType.Cyborg && attackDist > 160)
            {
                attackDist = 160;
            }

            if (world.Random.Next() < attackDist)
            {
                return false;
            }

            return true;
        }


        public void Chase(Mobj actor)
        {
            if (actor.ReactionTime > 0)
            {
                actor.ReactionTime--;
            }

            // Modify target threshold.
            if (actor.Threshold > 0)
            {
                if (actor.Target == null || actor.Target.Health <= 0)
                {
                    actor.Threshold = 0;
                }
                else
                {
                    actor.Threshold--;
                }
            }

            // Turn towards movement direction if not there yet.
            if ((int)actor.MoveDir < 8)
            {
                actor.Angle = new Angle((int)actor.Angle.Data & (7 << 29));

                var delta = (int)(actor.Angle - new Angle((int)actor.MoveDir << 29)).Data;

                if (delta > 0)
                {
                    actor.Angle -= new Angle(Angle.Ang90.Data / 2);
                }
                else if (delta < 0)
                {
                    actor.Angle += new Angle(Angle.Ang90.Data / 2);
                }
            }

            if (actor.Target == null || (actor.Target.Flags & MobjFlags.Shootable) == 0)
            {
                // Look for a new target.
                if (LookForPlayers(actor, true))
                {
                    // Got a new target.
                    return;
                }

                actor.SetState(actor.Info.SpawnState);

                return;
            }

            // Do not attack twice in a row.
            if ((actor.Flags & MobjFlags.JustAttacked) != 0)
            {
                actor.Flags &= ~MobjFlags.JustAttacked;

                if (world.Options.Skill != GameSkill.Nightmare &&
                    !world.Options.FastMonsters)
                {
                    NewChaseDir(actor);
                }

                return;
            }

            // Check for melee attack.
            if (actor.Info.MeleeState != 0 && CheckMeleeRange(actor))
            {
                if (actor.Info.AttackSound != 0)
                {
                    world.StartSound(actor, actor.Info.AttackSound);
                }

                actor.SetState(actor.Info.MeleeState);

                return;
            }

            // Check for missile attack.
            if (actor.Info.MissileState != 0)
            {
                if (world.Options.Skill < GameSkill.Nightmare &&
                    !world.Options.FastMonsters &&
                    actor.MoveCount != 0)
                {
                    goto noMissile;
                }

                if (!CheckMissileRange(actor))
                {
                    goto noMissile;
                }

                actor.SetState(actor.Info.MissileState);
                actor.Flags |= MobjFlags.JustAttacked;

                return;
            }

        noMissile:
            // Possibly choose another target.
            if (world.Options.NetGame &&
                actor.Threshold == 0 &&
                !world.VisibilityCheck.CheckSight(actor, actor.Target))
            {
                if (LookForPlayers(actor, true))
                {
                    // Got a new target.
                    return;
                }
            }

            // Chase towards player.
            if (--actor.MoveCount < 0 || !Move(actor))
            {
                NewChaseDir(actor);
            }

            // Make active sound.
            if (actor.Info.ActiveSound != 0 && world.Random.Next() < 3)
            {
                world.StartSound(actor, actor.Info.ActiveSound);
            }
        }


        public void FaceTarget(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            actor.Flags &= ~MobjFlags.Ambush;

            actor.Angle = Geometry.PointToAngle(
                actor.X, actor.Y,
                actor.Target.X, actor.Target.Y);

            var random = world.Random;

            if ((actor.Target.Flags & MobjFlags.Shadow) != 0)
            {
                actor.Angle += new Angle((random.Next() - random.Next()) << 21);
            }
        }


        public void PosAttack(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            FaceTarget(actor);

            var angle = actor.Angle;
            var slope = world.Hitscan.AimLineAttack(actor, angle, World.MISSILERANGE);

            world.StartSound(actor, Sfx.PISTOL);

            var random = world.Random;
            angle += new Angle((random.Next() - random.Next()) << 20);
            var damage = ((random.Next() % 5) + 1) * 3;

            world.Hitscan.LineAttack(actor, angle, World.MISSILERANGE, slope, damage);
        }


        public void SPosAttack(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            world.StartSound(actor, Sfx.SHOTGN);

            FaceTarget(actor);

            var center = actor.Angle;
            var slope = world.Hitscan.AimLineAttack(actor, center, World.MISSILERANGE);

            var random = world.Random;

            for (var i = 0; i < 3; i++)
            {
                var angle = center + new Angle((random.Next() - random.Next()) << 20);
                var damage = ((random.Next() % 5) + 1) * 3;

                world.Hitscan.LineAttack(actor, angle, World.MISSILERANGE, slope, damage);
            }
        }


        public void CPosAttack(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            world.StartSound(actor, Sfx.SHOTGN);

            FaceTarget(actor);

            var center = actor.Angle;
            var slope = world.Hitscan.AimLineAttack(actor, center, World.MISSILERANGE);

            var random = world.Random;
            var angle = center + new Angle((random.Next() - random.Next()) << 20);
            var damage = ((random.Next() % 5) + 1) * 3;

            world.Hitscan.LineAttack(actor, angle, World.MISSILERANGE, slope, damage);
        }


        public void CPosRefire(Mobj actor)
        {
            // Keep firing unless target got out of sight.
            FaceTarget(actor);

            if (world.Random.Next() < 40)
            {
                return;
            }

            if (actor.Target == null ||
                actor.Target.Health <= 0 ||
                !world.VisibilityCheck.CheckSight(actor, actor.Target))
            {
                actor.SetState(actor.Info.SeeState);
            }
        }


        public void Pain(Mobj actor)
        {
            if (actor.Info.PainSound != 0)
            {
                world.StartSound(actor, actor.Info.PainSound);
            }
        }


        public void Scream(Mobj actor)
        {
            int sound;

            switch (actor.Info.DeathSound)
            {
                case 0:
                    return;

                case Sfx.PODTH1:
                case Sfx.PODTH2:
                case Sfx.PODTH3:
                    sound = (int)Sfx.PODTH1 + world.Random.Next() % 3;
                    break;

                case Sfx.BGDTH1:
                case Sfx.BGDTH2:
                    sound = (int)Sfx.BGDTH1 + world.Random.Next() % 2;
                    break;

                default:
                    sound = (int)actor.Info.DeathSound;
                    break;
            }

            // Check for bosses.
            if (actor.Type == MobjType.Spider || actor.Type == MobjType.Cyborg)
            {
                // full volume
                world.StartSound(null, (Sfx)sound);
            }
            else
            {
                world.StartSound(actor, (Sfx)sound);
            }
        }


        public void XScream(Mobj actor)
        {
            world.StartSound(actor, Sfx.SLOP);
        }


        public void TroopAttack(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            FaceTarget(actor);

            if (CheckMeleeRange(actor))
            {
                world.StartSound(actor, Sfx.CLAW);

                var damage = (world.Random.Next() % 8 + 1) * 3;
                world.ThingInteraction.DamageMobj(actor.Target, actor, actor, damage);

                return;
            }

            // Launch a missile.
            world.ThingAllocation.SpawnMissile(actor, actor.Target, MobjType.Troopshot);
        }


        public void SargAttack(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            FaceTarget(actor);

            if (CheckMeleeRange(actor))
            {
                var damage = ((world.Random.Next() % 10) + 1) * 4;
                world.ThingInteraction.DamageMobj(actor.Target, actor, actor, damage);
            }
        }


        public void HeadAttack(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            FaceTarget(actor);

            if (CheckMeleeRange(actor))
            {
                var damage = (world.Random.Next() % 6 + 1) * 10;
                world.ThingInteraction.DamageMobj(actor.Target, actor, actor, damage);

                return;
            }

            // Launch a missile.
            world.ThingAllocation.SpawnMissile(actor, actor.Target, MobjType.Headshot);
        }


        public void BruisAttack(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            if (CheckMeleeRange(actor))
            {
                world.StartSound(actor, Sfx.CLAW);

                var damage = (world.Random.Next() % 8 + 1) * 10;
                world.ThingInteraction.DamageMobj(actor.Target, actor, actor, damage);

                return;
            }

            // Launch a missile.
            world.ThingAllocation.SpawnMissile(actor, actor.Target, MobjType.Bruisershot);
        }


        private static readonly Fixed skullSpeed = Fixed.FromInt(20);

        public void SkullAttack(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            var dest = actor.Target;

            actor.Flags |= MobjFlags.SkullFly;

            world.StartSound(actor, actor.Info.AttackSound);

            FaceTarget(actor);

            var angle = actor.Angle;
            actor.MomX = skullSpeed * Trig.Cos(angle);
            actor.MomY = skullSpeed * Trig.Sin(angle);

            var dist = Geometry.AproxDistance(dest.X - actor.X, dest.Y - actor.Y);

            var num = (dest.Z + (dest.Height >> 1) - actor.Z).Data;
            var den = dist.Data / skullSpeed.Data;
            if (den < 1)
            {
                den = 1;
            }

            actor.MomZ = new Fixed(num / den);
        }


        public void Explode(Mobj thingy)
        {
            world.ThingInteraction.RadiusAttack(thingy, thingy.Target, 128);
        }


        public void FatRaise(Mobj actor)
        {
            FaceTarget(actor);

            world.StartSound(actor, Sfx.MANATK);
        }


        private static readonly Angle fatSpread = Angle.Ang90 / 8;

        public void FatAttack1(Mobj actor)
        {
            FaceTarget(actor);

            var ta = world.ThingAllocation;

            // Change direction to...
            actor.Angle += fatSpread;
            ta.SpawnMissile(actor, actor.Target, MobjType.Fatshot);

            var missile = ta.SpawnMissile(actor, actor.Target, MobjType.Fatshot);
            missile.Angle += fatSpread;
            var angle = missile.Angle;
            missile.MomX = new Fixed(missile.Info.Speed) * Trig.Cos(angle);
            missile.MomY = new Fixed(missile.Info.Speed) * Trig.Sin(angle);
        }


        public void FatAttack2(Mobj actor)
        {
            FaceTarget(actor);

            var ta = world.ThingAllocation;

            // Now here choose opposite deviation.
            actor.Angle -= fatSpread;
            ta.SpawnMissile(actor, actor.Target, MobjType.Fatshot);

            var missile = ta.SpawnMissile(actor, actor.Target, MobjType.Fatshot);
            missile.Angle -= fatSpread * 2;
            var angle = missile.Angle;
            missile.MomX = new Fixed(missile.Info.Speed) * Trig.Cos(angle);
            missile.MomY = new Fixed(missile.Info.Speed) * Trig.Sin(angle);
        }


        public void FatAttack3(Mobj actor)
        {
            FaceTarget(actor);

            var ta = world.ThingAllocation;

            var missile1 = ta.SpawnMissile(actor, actor.Target, MobjType.Fatshot);
            missile1.Angle -= fatSpread / 2;
            var angle1 = missile1.Angle;
            missile1.MomX = new Fixed(missile1.Info.Speed) * Trig.Cos(angle1);
            missile1.MomY = new Fixed(missile1.Info.Speed) * Trig.Sin(angle1);

            var missile2 = ta.SpawnMissile(actor, actor.Target, MobjType.Fatshot);
            missile2.Angle += fatSpread / 2;
            var angle2 = missile2.Angle;
            missile2.MomX = new Fixed(missile2.Info.Speed) * Trig.Cos(angle2);
            missile2.MomY = new Fixed(missile2.Info.Speed) * Trig.Sin(angle2);
        }




        public void BabyMetal(Mobj mo)
        {
            world.StartSound(mo, Sfx.BSPWLK);
            Chase(mo);
        }

        public void SpidRefire(Mobj actor)
        {
            // keep firing unless target got out of sight
            FaceTarget(actor);

            if (world.Random.Next() < 10)
            {
                return;
            }

            if (actor.Target == null
                || actor.Target.Health <= 0
                || !world.VisibilityCheck.CheckSight(actor, actor.Target))
            {
                actor.SetState(actor.Info.SeeState);
            }
        }

        public void BspiAttack(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            FaceTarget(actor);

            // launch a missile
            world.ThingAllocation.SpawnMissile(actor, actor.Target, MobjType.Arachplaz);
        }









        //
        // PIT_VileCheck
        // Detect a corpse that could be raised.
        //
        private Mobj corpsehit;
        private Mobj vileobj;
        private Fixed viletryx;
        private Fixed viletryy;

        private bool PIT_VileCheck(Mobj thing)
        {
            if ((thing.Flags & MobjFlags.Corpse) == 0)
            {
                // not a monster
                return true;
            }

            if (thing.Tics != -1)
            {
                // not lying still yet
                return true;
            }

            if (thing.Info.Raisestate == MobjState.Null)
            {
                // monster doesn't have a raise state
                return true;
            }

            var maxdist = thing.Info.Radius + DoomInfo.MobjInfos[(int)MobjType.Vile].Radius;

            if (Fixed.Abs(thing.X - viletryx) > maxdist
                || Fixed.Abs(thing.Y - viletryy) > maxdist)
            {
                // not actually touching
                return true;
            }

            corpsehit = thing;
            corpsehit.MomX = corpsehit.MomY = Fixed.Zero;
            corpsehit.Height = new Fixed(corpsehit.Height.Data << 2);
            var check = world.ThingMovement.CheckPosition(corpsehit, corpsehit.X, corpsehit.Y);
            corpsehit.Height = new Fixed(corpsehit.Height.Data >> 2);

            if (!check)
            {
                // doesn't fit here
                return true;
            }

            // got one, so stop checking
            return false;
        }

        //
        // A_VileChase
        // Check for ressurecting a body
        //
        public void VileChase(Mobj actor)
        {
            if (actor.MoveDir != Direction.None)
            {
                // check for corpses to raise
                viletryx = actor.X + actor.Info.Speed * xSpeed[(int)actor.MoveDir];
                viletryy = actor.Y + actor.Info.Speed * ySpeed[(int)actor.MoveDir];

                var bm = world.Map.BlockMap;
                var maxRadius = GameConstants.MaxThingRadius * 2;
                var blockX1 = bm.GetBlockX(viletryx - maxRadius);
                var blockX2 = bm.GetBlockX(viletryx + maxRadius);
                var blockY1 = bm.GetBlockY(viletryy - maxRadius);
                var blockY2 = bm.GetBlockY(viletryy + maxRadius);

                vileobj = actor;
                for (var bx = blockX1; bx <= blockX2; bx++)
                {
                    for (var by = blockY1; by <= blockY2; by++)
                    {
                        // Call PIT_VileCheck to check
                        // whether object is a corpse
                        // that canbe raised.
                        if (!bm.IterateThings(bx, by, mo => PIT_VileCheck(mo)))
                        {
                            // got one!
                            var temp = actor.Target;
                            actor.Target = corpsehit;
                            FaceTarget(actor);
                            actor.Target = temp;

                            actor.SetState(MobjState.VileHeal1);
                            world.StartSound(corpsehit, Sfx.SLOP);
                            var info = corpsehit.Info;

                            corpsehit.SetState(info.Raisestate);
                            corpsehit.Height = new Fixed(corpsehit.Height.Data << 2);
                            corpsehit.Flags = info.Flags;
                            corpsehit.Health = info.SpawnHealth;
                            corpsehit.Target = null;

                            return;
                        }
                    }
                }
            }

            // Return to normal attack.
            Chase(actor);
        }

        //
        // A_VileStart
        //
        public void VileStart(Mobj actor)
        {
            world.StartSound(actor, Sfx.VILATK);
        }

        public void StartFire(Mobj actor)
        {
            world.StartSound(actor, Sfx.FLAMST);
            Fire(actor);
        }

        public void FireCrackle(Mobj actor)
        {
            world.StartSound(actor, Sfx.FLAME);
            Fire(actor);
        }

        public void Fire(Mobj actor)
        {
            var dest = actor.Tracer;
            if (dest == null)
            {
                return;
            }

            // don't move it if the vile lost sight
            if (!world.VisibilityCheck.CheckSight(actor.Target, dest))
            {
                return;
            }

            var an = dest.Angle; // >> ANGLETOFINESHIFT;

            world.ThingMovement.UnsetThingPosition(actor);
            actor.X = dest.X + Fixed.FromInt(24) * Trig.Cos(an);
            actor.Y = dest.Y + Fixed.FromInt(24) * Trig.Sin(an);
            actor.Z = dest.Z;
            world.ThingMovement.SetThingPosition(actor);
        }

        //
        // A_VileTarget
        // Spawn the hellfire
        //
        public void VileTarget(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            FaceTarget(actor);

            var fog = world.ThingAllocation.SpawnMobj(
                actor.Target.X,
                actor.Target.X,
                actor.Target.Z, MobjType.Fire);

            actor.Tracer = fog;
            fog.Target = actor;
            fog.Tracer = actor.Target;
            Fire(fog);
        }

        //
        // A_VileAttack
        //
        public void VileAttack(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            FaceTarget(actor);

            if (!world.VisibilityCheck.CheckSight(actor, actor.Target))
            {
                return;
            }

            world.StartSound(actor, Sfx.BAREXP);
            world.ThingInteraction.DamageMobj(actor.Target, actor, actor, 20);
            actor.Target.MomZ = new Fixed(1000 * Fixed.FracUnit / actor.Target.Info.Mass);

            var an = actor.Angle; // >> ANGLETOFINESHIFT;

            var fire = actor.Tracer;

            if (fire == null)
            {
                return;
            }

            // move the fire between the vile and the player
            fire.X = actor.Target.X - Fixed.FromInt(24) * Trig.Cos(an);
            fire.Y = actor.Target.Y - Fixed.FromInt(24) * Trig.Sin(an);
            world.ThingInteraction.RadiusAttack(fire, actor, 70);
        }









        //
        // A_SkelMissile
        //
        public void SkelMissile(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            FaceTarget(actor);
            actor.Z += Fixed.FromInt(16); // so missile spawns higher
            var mo = world.ThingAllocation.SpawnMissile(actor, actor.Target, MobjType.Tracer);
            actor.Z -= Fixed.FromInt(16); // back to normal

            mo.X += mo.MomX;
            mo.Y += mo.MomY;
            mo.Tracer = actor.Target;
        }

        private static Angle TRACEANGLE = new Angle(0xc000000);

        public void Tracer(Mobj actor)
        {
            if ((world.Options.GameTic & 3) != 0)
            {
                return;
            }

            // spawn a puff of smoke behind the rocket		
            world.Hitscan.SpawnPuff(actor.X, actor.Y, actor.Z);

            var th = world.ThingAllocation.SpawnMobj(
                actor.X - actor.MomX,
                actor.Y - actor.MomY,
                actor.Z, MobjType.Smoke);

            th.MomZ = Fixed.One;
            th.Tics -= world.Random.Next() & 3;
            if (th.Tics < 1)
            {
                th.Tics = 1;
            }

            // adjust direction
            var dest = actor.Tracer;

            if (dest == null || dest.Health <= 0)
            {
                return;
            }

            // change angle	
            var exact = Geometry.PointToAngle(
                actor.X, actor.Y,
                dest.X, dest.Y);

            if (exact != actor.Angle)
            {
                if (exact - actor.Angle > Angle.Ang180)
                {
                    actor.Angle -= TRACEANGLE;
                    if (exact - actor.Angle < Angle.Ang180)
                    {
                        actor.Angle = exact;
                    }
                }
                else
                {
                    actor.Angle += TRACEANGLE;
                    if (exact - actor.Angle > Angle.Ang180)
                    {
                        actor.Angle = exact;
                    }
                }
            }

            exact = actor.Angle;
            actor.MomX = new Fixed(actor.Info.Speed) * Trig.Cos(exact);
            actor.MomY = new Fixed(actor.Info.Speed) * Trig.Sin(exact);

            // change slope
            var dist = Geometry.AproxDistance(
                dest.X - actor.X,
                dest.Y - actor.Y);

            dist = new Fixed(dist.Data / actor.Info.Speed);

            if (dist < new Fixed(1))
            {
                dist = new Fixed(1);
            }

            var slope = new Fixed((dest.Z + Fixed.FromInt(40) - actor.Z).Data / dist.Data);

            if (slope < actor.MomZ)
            {
                actor.MomZ -= Fixed.One / 8;
            }
            else
            {
                actor.MomZ += Fixed.One / 8;
            }
        }


        public void SkelWhoosh(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            FaceTarget(actor);
            world.StartSound(actor, Sfx.SKESWG);
        }

        public void SkelFist(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            FaceTarget(actor);

            if (CheckMeleeRange(actor))
            {
                var damage = ((world.Random.Next() % 10) + 1) * 6;
                world.StartSound(actor, Sfx.SKEPCH);
                world.ThingInteraction.DamageMobj(actor.Target, actor, actor, damage);
            }
        }






        //
        // A_PainShootSkull
        // Spawn a lost soul and launch it at the target
        //
        public void PainShootSkull(Mobj actor, Angle angle)
        {
            // count total number of skull currently on the level
            var count = 0;

            foreach (var thinker in world.Thinkers)
            {
                var mobj = thinker as Mobj;
                if (mobj != null && mobj.Type == MobjType.Skull)
                {
                    count++;
                }
            }

            // if there are allready 20 skulls on the level,
            // don't spit another one
            if (count > 20)
            {
                return;
            }

            // okay, there's playe for another one
            var an = angle; // >> ANGLETOFINESHIFT;

            var prestep = Fixed.FromInt(4)
                + 3 * (actor.Info.Radius + DoomInfo.MobjInfos[(int)MobjType.Skull].Radius) / 2;

            var x = actor.X + prestep * Trig.Cos(an);
            var y = actor.Y + prestep * Trig.Sin(an);
            var z = actor.Z + Fixed.FromInt(8);

            var newmobj = world.ThingAllocation.SpawnMobj(x, y, z, MobjType.Skull);

            // Check for movements.
            if (!world.ThingMovement.TryMove(newmobj, newmobj.X, newmobj.Y))
            {
                // kill it immediately
                world.ThingInteraction.DamageMobj(newmobj, actor, actor, 10000);
                return;
            }

            newmobj.Target = actor.Target;
            SkullAttack(newmobj);
        }

        //
        // A_PainAttack
        // Spawn a lost soul and launch it at the target
        // 
        public void PainAttack(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            FaceTarget(actor);
            PainShootSkull(actor, actor.Angle);
        }


        public void PainDie(Mobj actor)
        {
            Fall(actor);
            PainShootSkull(actor, actor.Angle + Angle.Ang90);
            PainShootSkull(actor, actor.Angle + Angle.Ang180);
            PainShootSkull(actor, actor.Angle + Angle.Ang270);
        }




        public void Hoof(Mobj mo)
        {
            world.StartSound(mo, Sfx.HOOF);
            Chase(mo);
        }

        public void Metal(Mobj mo)
        {
            world.StartSound(mo, Sfx.METAL);
            Chase(mo);
        }

        public void CyberAttack(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            FaceTarget(actor);
            world.ThingAllocation.SpawnMissile(actor, actor.Target, MobjType.Rocket);
        }
    }
}
