using UnityEngine;

public class RoomManager : MonoBehaviour
{
    [Header("所有房间")]
    public Room[] allRooms;

    [Header("UI")]
    public GameObject roomClearText;  // 显示"房间已清理"的UI

    void Start()
    {
        // 初始时所有房间的门都是打开的
    }

    // 重置所有房间（进入新一层时调用）
    public void ResetAllRooms()
    {
        foreach (Room room in allRooms)
        {
            // 这里可以重置房间状态
        }
    }
}