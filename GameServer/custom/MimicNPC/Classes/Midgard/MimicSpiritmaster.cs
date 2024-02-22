﻿using DOL.GS.PlayerClass;

namespace DOL.GS.Scripts
{
    public class MimicSpiritmaster : MimicNPC
    {
        public MimicSpiritmaster(byte level) : base(new ClassSpiritmaster(), level)
        {
            MimicSpec = new SpiritmasterSpec();

            SpendSpecPoints();
            MimicEquipment.SetMeleeWeapon(this, MimicSpec.WeaponTypeOne, eHand.twoHand);
            SwitchWeapon(eActiveWeaponSlot.Standard);
            RefreshSpecDependantSkills(false);
            SetCasterSpells();
            RefreshItemBonuses();
            IsCloakHoodUp = Util.RandomBool();
        }
    }

    public class SpiritmasterSpec : MimicSpec
    {
        public SpiritmasterSpec()
        {
            SpecName = "SpiritmasterSpec";
            
            WeaponTypeOne = eObjectType.Staff;

            int randVariance = Util.Random(9);
            
            switch (randVariance)
            {
                case 0:
                case 1:
                Add(Specs.Darkness, 47, 1.0f);
                Add(Specs.Suppression, 5, 0.0f);
                Add(Specs.Summoning, 26, 0.1f);
                break;

                case 2:
                case 3:
                Add(Specs.Darkness, 47, 1.0f);
                Add(Specs.Suppression, 26, 0.1f);
                Add(Specs.Summoning, 6, 0.0f);
                break;

                case 4:
                case 5:
                Add(Specs.Darkness, 5, 0.0f);
                Add(Specs.Suppression, 49, 0.0f);
                Add(Specs.Summoning, 22, 0.1f);
                break;

                case 6:
                Add(Specs.Darkness, 35, 0.1f);
                Add(Specs.Suppression, 41, 1.0f);
                Add(Specs.Summoning, 3, 0.0f);
                break;

                case 7:
                Add(Specs.Darkness, 24, 0.1f);
                Add(Specs.Suppression, 6, 0.0f);
                Add(Specs.Summoning, 48, 1.0f);
                break;

                case 8:
                Add(Specs.Darkness, 28, 0.1f);
                Add(Specs.Suppression, 10, 0.0f);
                Add(Specs.Summoning, 45, 1.0f);
                break;

                case 9:
                Add(Specs.Darkness, 12, 0.0f);
                Add(Specs.Suppression, 43, 0.1f);
                Add(Specs.Summoning, 30, 1.0f);
                break;
            }
        }
    }
}