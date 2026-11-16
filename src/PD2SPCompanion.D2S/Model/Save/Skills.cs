using D2SLib.IO;
using System;
using System.Collections.Generic;
using System.Text;

namespace D2SLib.Model.Save
{
    public class ClassSkills
    {
        private static readonly UInt32[] SKILL_OFFSETS = { 6, 36, 66, 96, 126, 221, 251 };

        /// <summary>Vanilla stores thirty skills a class.</summary>
        public const int VANILLA_SKILLS = 30;

        /// <summary>
        /// PD2 stores three more, one per class from each of its three added skill blocks.
        /// They are the three bytes the converter fork used to skip past as reserved space
        /// before the item list - its own comment called them "reserved for new PD2 skills".
        /// </summary>
        public const int PD2_SKILLS = 33;

        public static int SkillCount => Globals.pd2_char_formatting ? PD2_SKILLS : VANILLA_SKILLS;

        /// <summary>Bytes the section occupies: the two-byte header, then one byte a skill.</summary>
        public static int SectionLength => 2 + SkillCount;
        public UInt16? Header { get; set; }
        public List<ClassSkill> Skills { get; set; } = new List<ClassSkill>();

        public ClassSkill this[int i] => this.Skills[i];

        public static ClassSkills Read(byte[] bytes, int playerClass)
        {
            ClassSkills classSkills = new ClassSkills();
            using(BitReader reader = new BitReader(bytes))
            {
                classSkills.Header = reader.ReadUInt16();
                uint offset = SKILL_OFFSETS[playerClass];
                for (uint i = 0; i < SkillCount; i++)
                {
                    ClassSkill skill = ClassSkill.Read(reader.ReadByte());
                    skill.Id = offset + i;
                    classSkills.Skills.Add(skill);
                }
                return classSkills;
            }
        }

        public static byte[] Write(ClassSkills classSkills)
        {
            using (BitWriter writer = new BitWriter())
            {
                writer.WriteUInt16(classSkills.Header ?? (UInt16)0x6669);
                for (int i = 0; i < classSkills.Skills.Count; i++)
                {
                    writer.WriteBytes(ClassSkill.Write(classSkills.Skills[i]));
                }
                return writer.ToArray();
            }
        }
    }

    public class ClassSkill
    {
        public UInt32 Id { get; set; }
        public byte Points { get; set; }

        public static ClassSkill Read(byte b)
        {
            ClassSkill classSkill = new ClassSkill();
            classSkill.Points = b;
            return classSkill;
        }

        public static byte[] Write(ClassSkill classSkill)
        {
            using (BitWriter writer = new BitWriter())
            {
                writer.WriteByte(classSkill.Points);
                return writer.ToArray();
            }
        }
    }

    //header skill
    public class Skill
    {
        public UInt32 Id { get; set; }
        public static Skill Read(byte[] bytes)
        {
            Skill skill = new Skill();
            skill.Id = BitConverter.ToUInt32(bytes, 0);
            return skill;
        }

        public static byte[] Write(Skill skill)
        {
            using (BitWriter writer = new BitWriter())
            {
                writer.WriteUInt32(skill.Id);
                return writer.ToArray();
            }
        }
    }
}
