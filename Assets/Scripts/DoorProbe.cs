using UnityEngine;

/// <summary>
/// 门上的引用计数。
///
/// 问题背景：场景里每扇门同时被两个房间的 doors 列表引用。
/// 旧逻辑里 Room 会无条件关掉自己列表里的**所有**门，清房时又全部打开，
/// 于是会出现"另一个房间的门被提前锁死／被提前解锁"互相打架，玩家被关在里面。
///
/// 现在改成谁想关门就 Claim 一次，谁清完房就 Release 一次；
/// 只有**没有任何房间**还要求关的时候，门才会真正打开。
/// </summary>
public class DoorProbe : MonoBehaviour
{
    private int claims = 0;

    public bool IsClaimed { get { return claims > 0; } }

    public void Claim()
    {
        claims++;
        Apply();
    }

    public void Release()
    {
        claims = Mathf.Max(0, claims - 1);
        Apply();
    }

    public void ForceReleaseAll()
    {
        claims = 0;
        Apply();
    }

    void Apply()
    {
        Door door = GetComponent<Door>();
        if (door == null) return;

        if (claims > 0) door.Close();
        else door.Open();
    }
}
