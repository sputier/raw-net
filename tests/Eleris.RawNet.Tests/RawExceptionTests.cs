using System.Globalization;

namespace Eleris.RawNet.Tests;

public class RawExceptionTests
{
    [Fact]
    public void Every_specific_error_derives_directly_from_RawException()
    {
        Type[] types =
        [
            typeof(UnsupportedFormatException),
            typeof(UnsupportedCameraException),
            typeof(UnsupportedCompressionException),
            typeof(CorruptRawFileException),
        ];

        Assert.All(types, t => Assert.Equal(typeof(RawException), t.BaseType));
        Assert.Equal(typeof(Exception), typeof(RawException).BaseType);
    }

    [Fact]
    public void An_unsupported_camera_names_the_make_and_the_model()
    {
        var error = new UnsupportedCameraException("Maker", "Model X");

        Assert.Equal("Maker", error.Make);
        Assert.Equal("Model X", error.Model);
        Assert.Equal("The camera 'Maker Model X' is not supported.", error.Message);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("  ", null)]
    public void An_unsupported_camera_without_a_name_still_has_a_message(string? make, string? model)
    {
        Assert.Equal("The camera is not supported.", new UnsupportedCameraException(make, model).Message);
    }

    [Fact]
    public void An_unsupported_camera_with_only_one_name_does_not_leave_a_gap()
    {
        Assert.Equal("The camera 'Maker' is not supported.", new UnsupportedCameraException("Maker", null).Message);
        Assert.Equal("The camera 'Model' is not supported.", new UnsupportedCameraException(null, "Model").Message);
    }

    [Fact]
    public void The_message_of_an_unsupported_camera_hides_control_characters_and_is_short_but_the_properties_stay_raw()
    {
        string hostile = "Evil\0\n\u001b[31mName" + new string('x', 500);

        var error = new UnsupportedCameraException(hostile, null);

        Assert.Equal(hostile, error.Make);
        Assert.DoesNotContain(error.Message, char.IsControl);
        Assert.InRange(error.Message.Length, 1, 120);
    }

    [Fact]
    public void An_unsupported_compression_carries_its_code_and_name()
    {
        var error = new UnsupportedCompressionException(34713, "NEF compressed");

        Assert.Equal(34713, error.Code);
        Assert.Equal("NEF compressed", error.Name);
        Assert.Equal("The compression 34713 (NEF compressed) is not supported.", error.Message);
    }

    [Fact]
    public void An_unnamed_compression_has_a_message_with_its_code_only()
    {
        var error = new UnsupportedCompressionException(99, null);

        Assert.Null(error.Name);
        Assert.Equal("The compression 99 is not supported.", error.Message);
    }

    [Fact]
    public void A_negative_compression_code_does_not_depend_on_the_culture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");

            Assert.Equal("The compression -5 is not supported.", new UnsupportedCompressionException(-5, null).Message);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(0L, "Bad tag (at byte 0)")]
    [InlineData(1234L, "Bad tag (at byte 1234)")]
    [InlineData(long.MaxValue, "Bad tag (at byte 9223372036854775807)")]
    public void A_corrupt_file_reports_the_position_when_it_is_known(long position, string message)
    {
        var error = new CorruptRawFileException("Bad tag", position);

        Assert.Equal(position, error.Position);
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void A_corrupt_file_without_a_position_keeps_the_message_as_it_is()
    {
        var error = new CorruptRawFileException("Bad tag");

        Assert.Null(error.Position);
        Assert.Equal("Bad tag", error.Message);
    }

    [Fact]
    public void Every_exception_that_can_wrap_a_cause_keeps_it()
    {
        var cause = new EndOfStreamException();

        Assert.Same(cause, new RawException("x", cause).InnerException);
        Assert.Same(cause, new UnsupportedFormatException("x", cause).InnerException);
        var corrupt = new CorruptRawFileException("x", 7, cause);
        Assert.Same(cause, corrupt.InnerException);
        Assert.Equal(7, corrupt.Position);
    }
}
