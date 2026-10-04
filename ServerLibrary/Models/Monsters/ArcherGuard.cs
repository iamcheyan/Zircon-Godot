using Library;
using Server.Envir;
using System;
using System.Collections.Generic;
using System.Drawing;
using S = Library.Network.ServerPackets;

namespace Server.Models.Monsters
{
    public sealed class ArcherGuard : MonsterObject
    {
        public override bool Blocking => true;
        public override bool CanMove => false;

        public int AttackRange = 15;

        public ArcherGuard()
        {
            NameColour = Color.SkyBlue;
        }

        public override void ProcessAction(DelayedAction action)
        {
            switch (action.Type)
            {
                case ActionType.DelayAttack:
                    MapObject target = (MapObject)action.Data[0];
                    if (target?.Node != null && !target.Dead && target.CurrentMap == CurrentMap)
                    {
                        target.Attacked(this, (int)action.Data[1], (Element)action.Data[2], canReflect: false, ignoreShield: true);
                    }
                    return;
            }

            base.ProcessAction(action);
        }

        public override void ProcessSearch()
        {
            ProperSearch();
        }

        protected override bool InAttackRange()
        {
            return Target != null && Target.CurrentMap == CurrentMap && Functions.InRange(CurrentLocation, Target.CurrentLocation, AttackRange);
        }

        public override void ProcessNameColour()
        {
            NameColour = Color.SkyBlue;
        }

        public override int Attacked(MapObject ob, int power, Element element, bool canReflect = true, bool ignoreShield = false, bool canCrit = true, bool canStruck = true)
        {
            return 0;
        }

        public override bool ShouldAttackTarget(MapObject ob)
        {
            return CanAttackTarget(ob);
        }

        public override bool CanAttackTarget(MapObject ob)
        {
            if (ob?.Node == null || ob.Dead || !ob.Visible || ob is Guard || ob is ArcherGuard || ob is CastleLord) return false;

            switch (ob.Race)
            {
                case ObjectType.Player:
                    return ob.Stats[Stat.PKPoint] >= Config.RedPoint && ob.Stats[Stat.Redemption] == 0;

                case ObjectType.Monster:
                    MonsterObject mob = (MonsterObject)ob;
                    if (mob.PetOwner == null)
                        return !mob.Passive;

                    if (mob.PetOwner.Stats[Stat.PKPoint] >= Config.RedPoint && mob.PetOwner.Stats[Stat.Redemption] == 0)
                        return true;
                    return false;

                default:
                    return false;
            }
        }

        protected override void Attack()
        {
            if (!CanAttackTarget(Target))
            {
                Target = null;
                return;
            }

            Direction = Functions.DirectionFromPoint(CurrentLocation, Target.CurrentLocation);
            Broadcast(new S.ObjectRangeAttack { ObjectID = ObjectID, Direction = Direction, Location = CurrentLocation, Targets = new List<uint> { Target.ObjectID } });

            UpdateAttackTime();

            int travelTime = 300 + Functions.Distance(CurrentLocation, Target.CurrentLocation) * Globals.ProjectileSpeed;

            ActionList.Add(new DelayedAction(
                               SEnvir.Now.AddMilliseconds(travelTime),
                               ActionType.DelayAttack,
                               Target,
                               5000,
                               Element.None));
        }
    }
}
