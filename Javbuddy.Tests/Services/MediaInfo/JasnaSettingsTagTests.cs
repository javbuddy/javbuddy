using Javbuddy.Services.MediaInfo;

namespace Javbuddy.Tests.Services.MediaInfo;

public class JasnaSettingsTagTests
{
    /// <summary>The comment tag of a real Jasna 0.12.3 LTX output, as MediaInfoLib's General "Comment" returns it.</summary>
    public const string LtxTag = """{"jasna":"0.12.3","schema":1,"detection":{"model":"rfdetr-v6","threshold":0.35,"max_gap":2,"min_duration":2,"scene_cuts":true},"ltx":{"weights":"ltx-restore-alpha1-distill8-int8.safetensors","weights_release":"0.12.1","vae_encoder":"full","vae_decoder":"full","window_hop":104,"large_canvas":false,"seed":20260923},"output":{"codec":"hevc","lut":null,"sharpen":0.0,"retarget_high_fps":false,"frame_generation":"none","upscale":null,"encoder":"hevc_nvenc","encoder_options":{"preset":"p5","tune":"hq","profile":"main10","rc":"vbr","cq":"22","qmin":"17","qmax":"34","nonref_p":"1","g":"250","temporal-aq":"1","rc-lookahead":"32","lookahead_level":"1","spatial_aq":"1","aq-strength":"8","init_qpI":"17","init_qpP":"17","init_qpB":"17","bf":"4","b_ref_mode":"middle","maxrate":"5741232","bufsize":"11482464"}}}""";

    // Shape from Jasna's settings_tag.py: both models over segments, an upscale, and the source's own comment.
    private const string MixedTag = """{"jasna":"0.13.0","schema":1,"detection":{"model":"rfdetr-v6","threshold":0.3,"max_gap":2,"min_duration":2,"scene_cuts":false},"basicvsrpp":{"model":"lada_mosaic_restoration_model_generic_v1.2.pth","fp16":true,"tensorrt":true,"max_clip_size":180,"temporal_overlap":8,"denoise":"none","secondary":{"name":"none"},"vr":"off"},"ltx":{"weights":"w.safetensors","weights_release":"0.12.1","vae_encoder":"full","vae_decoder":"full","window_hop":104,"large_canvas":true},"auto_split_percent":40,"segments":{"count":3,"basicvsrpp":{"count":1,"seconds":4.0},"ltx":{"count":2,"seconds":12.5,"seeds":[7,9]}},"output":{"codec":"hevc","lut":"film.cube","sharpen":0.2,"retarget_high_fps":false,"frame_generation":"none","upscale":{"model":"rtx-super-res","height":2160,"strength":1.0},"encoder":"hevc_nvenc","encoder_options":{"preset":"p5"}},"source_comment":"ripped by someone"}""";

    [Fact]
    public void Parse_RealLtxTag_ReadsVersionSchemaAndModel()
    {
        var settings = JasnaSettingsTag.Parse(LtxTag);

        Assert.NotNull(settings);
        Assert.Equal("0.12.3", settings.Version);
        Assert.Equal(1, settings.Schema);
        Assert.Equal("ltx", settings.Restoration);
        Assert.Equal(LtxTag, settings.Json);
    }

    [Fact]
    public void Parse_BothModels_JoinsThemSorted()
    {
        Assert.Equal("basicvsrpp+ltx", JasnaSettingsTag.Parse(MixedTag)?.Restoration);
        Assert.Equal("BasicVSR++ + LTX", JasnaSettingsTag.RestorationLabel("basicvsrpp+ltx"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Encoded by HandBrake")]
    [InlineData("{\"title\":\"not jasna\"}")]
    [InlineData("{\"jasna\":12}")]
    [InlineData("{\"jasna\":\"0.12.3\",")]
    [InlineData("[1,2]")]
    public void Parse_NotAJasnaTag_ReturnsNull(string? comment) => Assert.Null(JasnaSettingsTag.Parse(comment));

    [Theory]
    [InlineData("{\"jasna\":\"1\",\"jasna\":\"2\"}")]
    [InlineData("{\"jasna\":\"1\",\"x\":1,\"x\":2}")]
    public void Parse_DuplicateKey_ReturnsNullInsteadOfThrowing(string comment) => Assert.Null(JasnaSettingsTag.Parse(comment));

    [Fact]
    public void ReportSections_DuplicateKeyInsideABlock_IsEmptyInsteadOfThrowing() =>
        Assert.Empty(JasnaSettingsTag.ReportSections("{\"jasna\":\"1\",\"output\":{\"codec\":\"hevc\",\"codec\":\"av1\"}}"));

    [Fact]
    public void Parse_EscapedJapaneseSourceCommentOverOldCap_IsStillJasna()
    {
        // Jasna's json.dumps escapes non-ASCII: a 10 000-character Japanese source comment becomes 60 000.
        var escaped = string.Concat(Enumerable.Repeat("\\u3042", 10_000));
        var tag = "{\"jasna\":\"0.12.3\",\"schema\":1,\"ltx\":{\"weights\":\"w\"},\"source_comment\":\"" + escaped + "\"}";

        Assert.True(tag.Length > 60_000);
        Assert.Equal("0.12.3", JasnaSettingsTag.Parse(tag)?.Version);
    }

    [Fact]
    public void Parse_Oversized_ReturnsNull() =>
        Assert.Null(JasnaSettingsTag.Parse("{\"jasna\":\"1\",\"x\":\"" + new string('a', JasnaSettingsTag.MaxLength) + "\"}"));

    [Fact]
    public void Parse_UnknownModelBlock_CountsAsAModel() =>
        Assert.Equal("futurenet", JasnaSettingsTag.Parse("""{"jasna":"2.0.0","schema":2,"futurenet":{"steps":4},"output":{"codec":"av1"}}""")?.Restoration);

    [Fact]
    public void ReportSections_RealLtxTag_ShowsSettingsAndSkipsOutputLeftOff()
    {
        var sections = JasnaSettingsTag.ReportSections(LtxTag).ToDictionary(s => s.Title, s => s.Lines.ToDictionary(l => l.Label, l => l.Value));

        Assert.Equal(["Jasna", "Jasna: LTX", "Jasna: Output"], sections.Keys);
        Assert.Equal("0.12.3 (schema 1)", sections["Jasna"]["Version"]);
        Assert.Equal("LTX", sections["Jasna"]["Restoration"]);
        Assert.Equal("rfdetr-v6", sections["Jasna"]["Detection model"]);
        Assert.Equal("0.35", sections["Jasna"]["Detection threshold"]);
        Assert.Equal("Yes", sections["Jasna"]["Detection scene cuts"]);
        Assert.Equal("ltx-restore-alpha1-distill8-int8.safetensors", sections["Jasna: LTX"]["Weights"]);
        Assert.Equal("No", sections["Jasna: LTX"]["Large canvas"]);
        Assert.Equal("full", sections["Jasna: LTX"]["VAE encoder"]);
        Assert.Equal("20260923", sections["Jasna: LTX"]["Seed"]);
        Assert.Equal("hevc (hevc_nvenc)", sections["Jasna: Output"]["Codec"]);
        Assert.StartsWith("preset=p5 / tune=hq / profile=main10", sections["Jasna: Output"]["Encoder settings"]);
        Assert.DoesNotContain("LUT", sections["Jasna: Output"].Keys);
        Assert.DoesNotContain("Sharpen", sections["Jasna: Output"].Keys);
        Assert.DoesNotContain("Frame generation", sections["Jasna: Output"].Keys);
        Assert.DoesNotContain("Upscale", sections["Jasna: Output"].Keys);
    }

    [Fact]
    public void ReportSections_MixedTag_ShowsSegmentsModelsUpscaleAndSourceComment()
    {
        var sections = JasnaSettingsTag.ReportSections(MixedTag).ToDictionary(s => s.Title, s => s.Lines.ToDictionary(l => l.Label, l => l.Value));

        Assert.Equal(["Jasna", "Jasna: BasicVSR++", "Jasna: LTX", "Jasna: Output"], sections.Keys);
        Assert.Equal("40%", sections["Jasna"]["Auto split"]);
        Assert.Equal("3", sections["Jasna"]["Segments"]);
        Assert.Equal("count=2 / seconds=12.5 / seeds=7, 9", sections["Jasna"]["Segments (LTX)"]);
        Assert.Equal("ripped by someone", sections["Jasna"]["Source comment"]);
        Assert.Equal("name=none", sections["Jasna: BasicVSR++"]["Secondary"]);
        Assert.Equal("film.cube", sections["Jasna: Output"]["LUT"]);
        Assert.Equal("0.2", sections["Jasna: Output"]["Sharpen"]);
        Assert.Equal("model=rtx-super-res / height=2160 / strength=1", sections["Jasna: Output"]["Upscale"]);
    }

    [Fact]
    public void ReportSections_NotJasna_IsEmpty() => Assert.Empty(JasnaSettingsTag.ReportSections("{\"a\":1}"));
}
