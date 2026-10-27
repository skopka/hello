using Skopka.Hello;

namespace Skopka.Hello.Tests;

public sealed class HelloPhoneNormalizerTests
{
    [Theory]
    [InlineData("8 999 123-45-67", "+79991234567")]
    [InlineData("79991234567", "+79991234567")]
    [InlineData("9991234567", "+79991234567")]
    [InlineData("+7 (999) 123-45-67", "+79991234567")]
    [InlineData("+79991234567", "+79991234567")]
    [InlineData("+1 (212) 555-1234", "+12125551234")]
    [InlineData("8 999 123-45", "8 999 123-45")]
    [InlineData("8 999 abc-45-67", "8 999 abc-45-67")]
    [InlineData("8 999 123-45-67 ext 1", "8 999 123-45-67 ext 1")]
    [InlineData("8\u00a0999 123-45-67", "+79991234567")]
    [InlineData("8\t9991234567", "8\t9991234567")]
    public void RussianRegionNormalizesOnlyRecognizedInput(string input, string expected)
        => Assert.Equal(expected, HelloPhoneNormalizer.Normalize(input, "ru"));

    [Fact]
    public void DisabledPreservesInput()
        => Assert.Equal("8 999 123-45-67", HelloPhoneNormalizer.Normalize("8 999 123-45-67", null));

    [Fact]
    public void NullIsPassedToValidationAsEmpty()
        => Assert.Equal(string.Empty, HelloPhoneNormalizer.Normalize(null, "RU"));

    [Fact]
    public void UnknownRegionIsRejected()
        => Assert.Throws<InvalidOperationException>(() => new SkopkaHelloOptions { DefaultPhoneRegion = "US" }.Validate());
}
