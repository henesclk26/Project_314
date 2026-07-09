using System.Collections.Generic;
using UnityEngine;

public enum ComputerType
{
    Email,
    Password
}

[System.Serializable]
public class EmailData
{
    public string sender = "Unknown";
    public string subject = "No Subject";
    public string time = "00:00";
    [TextArea(5, 10)]
    public string body = "";
}

[CreateAssetMenu(fileName = "NewComputerData", menuName = "Mission/Computer Data")]
public class ComputerData : ScriptableObject
{
    [Header("General Settings")]
    public string computerName = "TERMINAL";
    public ComputerType computerType = ComputerType.Email;

    [Header("Email Settings (If Type is Email)")]
    public List<EmailData> emails = new List<EmailData>();

    [Header("Password Settings (If Type is Password)")]
    public string correctPassword = "";
    public string successMessage = "ACCESS GRANTED";
}
