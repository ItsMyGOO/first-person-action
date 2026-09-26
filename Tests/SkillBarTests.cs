using FirstPersonAction.Combat;
using Godot;
using Xunit;

namespace FirstPersonAction.Tests;

public class SkillBarLogicTests
{
    [Theory]
    [InlineData(0f, "")] // 就绪不显示
    [InlineData(-0.5f, "")] // 负数防御
    [InlineData(9.94f, "9.9")] // <10s：一位小数
    [InlineData(4.0f, "4.0")]
    [InlineData(10f, "10")] // ≥10s：整数（向上取整）
    [InlineData(12.3f, "13")]
    public void FormatSeconds_DisplaysCorrectText(float remaining, string expected)
    {
        Assert.Equal(expected, SkillBarLogic.FormatSeconds(remaining));
    }

    [Fact]
    public void Cooldown01_ClampsAndGuardsZeroTotal()
    {
        Assert.Equal(0f, SkillBarLogic.Cooldown01(3f, 0f)); // 除零守卫
        Assert.Equal(0.5f, SkillBarLogic.Cooldown01(3f, 6f));
        Assert.Equal(1f, SkillBarLogic.Cooldown01(9f, 6f)); // 上界钳制
        Assert.Equal(0f, SkillBarLogic.Cooldown01(-1f, 6f)); // 下界钳制
    }

    [Theory]
    [InlineData("1", "1")] // 数字键显示纯数字（龙之谷式）
    [InlineData("Key1", "1")]
    [InlineData("Key9", "9")]
    [InlineData("Key0", "0")]
    [InlineData("F", "F")] // 字母键原样
    [InlineData("Shift", "Shift")]
    public void FormatKey_DisplaysDigitKeysAsDigits(string keyName, string expected)
    {
        Assert.Equal(expected, SkillBarLogic.FormatKey(System.Enum.Parse<Key>(keyName)));
    }

    [Fact]
    public void FormatKey_NullBinding_ShowsDash()
    {
        Assert.Equal("-", SkillBarLogic.FormatKey(null));
    }
}
