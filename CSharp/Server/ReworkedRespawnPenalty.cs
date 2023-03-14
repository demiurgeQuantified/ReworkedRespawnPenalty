using System;
using System.Linq;
using System.Xml.Linq;
using System.Reflection;
using System.Collections.Generic;
using Barotrauma;
using Barotrauma.IO;
using Barotrauma.Networking;

// ReSharper disable CheckNamespace

namespace ReworkedRespawnPenalty {
    class ReworkedRespawnPenaltyMod : ACsMod {
        private const float PostDeathSkillMult = 3;
        private const float RepeatedDeathSkillDecay = 5;
        
        private Dictionary<int, Dictionary<Identifier, float>> predeathData = new();
        
        public ReworkedRespawnPenaltyMod() {
            GameMain.LuaCs.Hook.Patch("Barotrauma.CharacterInfo", "IncreaseSkillLevel",
                (self, args) => {
                    IncreaseSkillLevel((CharacterInfo)self, args);
                    return null;
                });
            GameMain.LuaCs.Hook.Patch("Barotrauma.Networking.RespawnManager", "ReduceCharacterSkills",
                (_, args) => {
                    ReduceCharacterSkills((CharacterInfo)args["characterInfo"]);
                    return null;
                });
            GameMain.LuaCs.Hook.Patch("Barotrauma.MultiPlayerCampaign", "SavePlayers",
                (_, args) => {
                    ToFile();
                    return null;
                });
            GameMain.LuaCs.Hook.Add("roundStart", "RRP_LoadData", (_) =>
            {
                FromFile();
                return null;
            });
        }
        
        private void IncreaseSkillLevel(CharacterInfo self, LuaCsHook.ParameterTable args)
        {
            if (self.Job == null || self.Character is not { IsPlayer: true } || self.Character.CharacterHealth.GetAffliction("reaperstax") != null) return;

            Dictionary<Identifier, float>? deathData;
            if (!predeathData.TryGetValue(self.GetIdentifier(), out deathData)) return;

            var skillIdentifier = (Identifier)args["skillIdentifier"];

            if (self.Job.GetSkillLevel(skillIdentifier) < deathData[skillIdentifier])
            {
                args["increase"] = (float)args["increase"] * PostDeathSkillMult;
            }
        }

        private void ReduceCharacterSkills(CharacterInfo characterInfo)
        {
            if (characterInfo.Job == null) return;

            Dictionary<Identifier, float> preDeathStats = new();

            foreach (Skill skill in characterInfo.Job.GetSkills())
            {
                preDeathStats[skill.Identifier] = skill.Level;
            }
            
            Dictionary<Identifier, float>? previousData;
            if (predeathData.TryGetValue(characterInfo.GetIdentifier(), out previousData))
            {
                foreach (var (k, v) in preDeathStats)
                {
                    float previousValue;
                    if (!previousData.TryGetValue(k, out previousValue)) continue;
                    previousValue -= RepeatedDeathSkillDecay;
                    if (v < previousValue) preDeathStats[k] = previousValue;
                }
            }
            
            predeathData[characterInfo.GetIdentifier()] = preDeathStats;
        }

        private static string GetFilename()
        {
            return GetStoreFolder<ReworkedRespawnPenaltyMod>() +
                   Path.DirectorySeparatorChar +
                   Path.GetFileNameWithoutExtension(GameMain.GameSession.SavePath) +
                   ".xml";
        }

        private void FromFile()
        {
            var doc = XMLExtensions.TryLoadXml(GetFilename());
            if (doc?.Root == null) return;
            foreach (var characterElement in doc.Root.Elements())
            {
                var charId = characterElement.Attribute("identifier");
                if (charId == null) continue;
                charId.Remove();
                
                var charData = new Dictionary<Identifier, float>();
                foreach (var skillElement in characterElement.Attributes())
                {
                    charData[skillElement.Name.ToIdentifier()] = Convert.ToSingle(skillElement.Value);
                }
                
                predeathData[Convert.ToInt32(charId.Value)] = charData;
            }
        }
        
        private void ToFile()
        {
            var doc = new XDocument(new XElement("predeathData"));
            foreach (var (characterIdentifier, data) in predeathData)
            {
                var characterElement = new XElement("Character",
                    new XAttribute("identifier", characterIdentifier));
                
                foreach (var (skillIdentifier, level) in data)
                {
                    characterElement.Add(new XAttribute(skillIdentifier.ToString(), level));
                }
                doc.Root.Add(characterElement);
            }
            
            var settings = new System.Xml.XmlWriterSettings
            {
                Indent = true,
                NewLineOnAttributes = true
            };

            using var writer = XmlWriter.Create(GetFilename(), settings);
            doc.SaveSafe(writer);
        }
        
        public override void Stop() { }
    }
}