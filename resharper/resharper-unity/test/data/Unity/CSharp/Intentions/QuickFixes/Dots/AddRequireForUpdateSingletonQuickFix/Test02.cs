using Unity.Entities;

struct Bar : IComponentData, IEnableableComponent
{
}

partial struct Foo : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var k = SystemAPI.GetSingleton{caret}<Bar>();//Must be marked with warning
    }

    public void OnDestroy(ref SystemState state)
    {
        
    }
}
