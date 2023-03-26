﻿using System;
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
        private bool boostSuspended = false;
        
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
            if ((bool)args["gainedFromAbility"] || self.Job == null || self.Character is not { IsPlayer: true } || self.Character.CharacterHealth.GetAffliction("reaperstax") != null) return;

            if (!predeathData.TryGetValue(self.GetIdentifier(), out Dictionary<Identifier, float>? deathData)) return;

            Identifier skillIdentifier = (Identifier)args["skillIdentifier"];
            float skillGap = deathData[skillIdentifier] - self.Job.GetSkillLevel(skillIdentifier);

            if (skillGap > 0f)
            {
                args["increase"] = (float)args["increase"] + skillGap * (PostDeathSkillMult - 1);
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
            
            if (predeathData.TryGetValue(characterInfo.GetIdentifier(), out Dictionary<Identifier, float>? previousData))
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