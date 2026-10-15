using UnityEngine;
using TMPro;

public class UIManager : MonoBehaviour
{
    public TextMeshProUGUI hanziText;
    public HanziSystem hanziSystem;

    void Update()
    {
        if (hanziSystem != null && hanziText != null)
        {
            hanziText.text = GameText.Panel.当前汉字标签() + hanziSystem.currentHanziName;
        }
    }
}