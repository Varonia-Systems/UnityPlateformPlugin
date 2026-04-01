using UnityEngine;

public class LabelAttribute : PropertyAttribute
{
    public string Name;
    public LabelAttribute(string name) => Name = name;
}