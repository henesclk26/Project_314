using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewTaskDefinition", menuName = "Project 314/Task Definition")]
public class TaskDefinition : ScriptableObject
{
    public string TaskID; // Must be unique, e.g., "MissionComputer", "WaveFrequency"
    public string DisplayName; // e.g., "Calibrate Wave Frequency"
    public string RoomName; // e.g., "Communications"
    
    public bool IsCooperative = false;
    public int RequiredVillagers = 1;
    public int MinLivingVillagersToOffer = 1;
    
    public bool KillerCanReceive = true;
    public bool IsSpecialMapSequence = false;
    
    public string SharedObjectSessionGroup = "";
}
