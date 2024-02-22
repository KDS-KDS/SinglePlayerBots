﻿using DOL.GS.PlayerClass;

namespace DOL.GS.Scripts
{
    public class MimicRanger : MimicNPC
    {
        public MimicRanger(byte level) : base(new ClassRanger(), level)
        {
            MimicSpec = new RangerSpec();

            SpendSpecPoints();
            MimicEquipment.SetRangedWeapon(this, eObjectType.RecurvedBow);
            MimicEquipment.SetMeleeWeapon(this, MimicSpec.WeaponTypeOne, eHand.oneHand);
            MimicEquipment.SetMeleeWeapon(this, MimicSpec.WeaponTypeOne, eHand.leftHand);
            SwitchWeapon(eActiveWeaponSlot.Distance);
            RefreshSpecDependantSkills(false);
            GetTauntStyles();
            SetSpells();
            RefreshItemBonuses();
            IsCloakHoodUp = Util.RandomBool();
        }
    }

    public class RangerSpec : MimicSpec
    {
        public RangerSpec()
        {
            SpecName = "RangerSpec";
            is2H = false;

            int randBaseWeap = Util.Random(1);

            switch (randBaseWeap)
            {
                case 0: WeaponTypeOne = eObjectType.Blades; break;
                case 1: WeaponTypeOne = eObjectType.Piercing; break;
            }
            
            int randVariance = Util.Random(7);
            
            switch (randVariance)
            {
                case 0:
                case 1:
                Add(ObjToSpec(WeaponTypeOne), 32, 0.4f);
                Add(Specs.RecurveBow, 35, 0.9f);
                Add(Specs.Pathfinding, 40, 0.5f);
                Add(Specs.Celtic_Dual, 29, 0.3f);
                Add(Specs.Stealth, 35, 0.2f);
                break;

                case 2:
                case 3:
                Add(ObjToSpec(WeaponTypeOne), 35, 0.4f);
                Add(Specs.RecurveBow, 35, 0.9f);
                Add(Specs.Pathfinding, 36, 0.5f);
                Add(Specs.Celtic_Dual, 31, 0.3f);
                Add(Specs.Stealth, 35, 0.2f);
                break;

                case 4:
                case 5:
                Add(ObjToSpec(WeaponTypeOne), 27, 0.4f);
                Add(Specs.RecurveBow, 45, 0.9f);
                Add(Specs.Pathfinding, 40, 0.5f);
                Add(Specs.Celtic_Dual, 19, 0.3f);
                Add(Specs.Stealth, 35, 0.2f);
                break;

                case 6:
                Add(ObjToSpec(WeaponTypeOne), 35, 0.6f);
                Add(Specs.Pathfinding, 42, 0.5f);
                Add(Specs.Celtic_Dual, 40, 1.0f);
                Add(Specs.Stealth, 35, 0.2f);
                break;

                case 7:
                Add(ObjToSpec(WeaponTypeOne), 25, 0.6f);
                Add(Specs.Pathfinding, 40, 0.5f);
                Add(Specs.Celtic_Dual, 50, 1.0f);
                Add(Specs.Stealth, 33, 0.2f);
                break;
            }
        }
    }
}