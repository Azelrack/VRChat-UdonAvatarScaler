using UnityEditor;
using UnityEngine;

namespace Azelrack.AvatarScaler
{
    /// <summary>
    /// Convert enums to int to keep serialization compatible with package assembly.
    /// </summary>
    [CustomPropertyDrawer(typeof(EnumToIntAttribute))]
    public class EnumToIntPropertyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType == SerializedPropertyType.Integer)
                property.intValue = (int)(ScalingMode)EditorGUI.EnumPopup(
                    position,
                    label,
                    (ScalingMode)property.intValue
                );
            else
                EditorGUI.LabelField(position, label.text);
        }
    }
}
