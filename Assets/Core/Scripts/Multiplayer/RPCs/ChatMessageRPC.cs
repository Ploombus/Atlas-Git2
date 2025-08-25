using Unity.NetCode;
using Unity.Collections;
using Unity.Entities;

public struct ChatMessageRPC : IRpcCommand
{
    public FixedString64Bytes message;
}
public struct RequestMessage : IComponentData
{
    public FixedString64Bytes message;
}

public struct ChatUserId : IRpcCommand
{
    public int userId;
}

public struct ChatUserInit : IComponentData { }