using System;
using Xunit;

namespace AmwalPay.Ecr.Tests
{
    public class EcrConfigTests
    {
        [Fact]
        public void DefaultsDescribeAnOmaniDeploymentOnTheStandardPort()
        {
            var config = new EcrConfig();

            Assert.Equal("ECR01", config.EcrId);
            Assert.Equal("512", config.CurrencyCode);
            Assert.Equal(3, config.MinorUnitDigits);
            Assert.Equal(9100, config.Port);
            Assert.Equal(EcrConfig.DefaultPort, config.Port);
            Assert.Equal(TimeSpan.FromSeconds(10), config.ConnectTimeout);
            Assert.Equal(TimeSpan.FromSeconds(120), config.ResponseTimeout);
            Assert.Equal(TimeSpan.FromSeconds(3), config.ProbeTimeout);
            Assert.Equal("", config.SecureHashKey);
            Assert.Equal("", config.MerchantId);
            Assert.Equal("", config.TerminalId);
            Assert.Equal(EcrEnvironment.SIT, config.Environment);
            Assert.True(config.AutoInquireOnFailure);
            Assert.False(config.SignsMessages);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(5)]
        [InlineData(100)]
        public void MinorUnitDigitsOutsideZeroToFourIsRefused(int digits)
        {
            var e = Assert.Throws<ArgumentException>(() => new EcrConfig { MinorUnitDigits = digits });

            Assert.Contains("minorUnitDigits", e.Message);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void MinorUnitDigitsZeroToFourIsAccepted(int digits)
        {
            Assert.Equal(digits, new EcrConfig { MinorUnitDigits = digits }.MinorUnitDigits);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        [InlineData(65536)]
        public void APortOutsideTheValidRangeIsRefused(int port)
        {
            var e = Assert.Throws<ArgumentException>(() => new EcrConfig { Port = port });

            Assert.Contains("port", e.Message);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(9100)]
        [InlineData(65535)]
        public void APortInsideTheValidRangeIsAccepted(int port)
        {
            Assert.Equal(port, new EcrConfig { Port = port }.Port);
        }

        [Theory]
        [InlineData("nothex")]
        [InlineData("abc")]
        [InlineData("aabb")]
        [InlineData("aabbccddeeff001")]          // 15 chars: odd and too short
        [InlineData("aabbccddeeff0011223")]      // 19 chars: odd
        [InlineData("aabbccddeeff00")]           // 14 chars: even but shorter than 16
        [InlineData("gggggggggggggggggggggggg")] // right length, not hex
        [InlineData(" aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // stray space
        [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n")] // stray newline
        public void AMalformedKeyIsRefusedAtAssignment(string key)
        {
            var e = Assert.Throws<ArgumentException>(() => new EcrConfig { SecureHashKey = key });

            Assert.Contains("secureHashKey", e.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData("aaaaaaaaaaaaaaaa")] // exactly 16 characters
        [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        [InlineData("0123456789abcdefABCDEF00")]
        public void AWellFormedKeyOrNoKeyIsAccepted(string key)
        {
            Assert.Equal(key, new EcrConfig { SecureHashKey = key }.SecureHashKey);
        }

        [Fact]
        public void ANullKeyMeansUnsigned()
        {
            var config = new EcrConfig { SecureHashKey = null! };

            Assert.Equal("", config.SecureHashKey);
            Assert.False(config.SignsMessages);
        }

        [Fact]
        public void IsValidSecureHashKeyAgreesWithTheConstructor()
        {
            Assert.True(EcrConfig.IsValidSecureHashKey(null));
            Assert.True(EcrConfig.IsValidSecureHashKey(""));
            Assert.True(EcrConfig.IsValidSecureHashKey(EcrTestConfigs.SecureHashKeyEcrWifi));
            Assert.False(EcrConfig.IsValidSecureHashKey("abc"));
            Assert.False(EcrConfig.IsValidSecureHashKey("not hex at all!!"));
        }

        [Fact]
        public void WithCopiesEveryOtherValueAcross()
        {
            var original = new EcrConfig
            {
                EcrId = "TILL-1",
                CurrencyCode = "840",
                MinorUnitDigits = 2,
                Port = 9200,
                ConnectTimeout = TimeSpan.FromSeconds(4),
                ResponseTimeout = TimeSpan.FromSeconds(30),
                ProbeTimeout = TimeSpan.FromSeconds(1),
                SecureHashKey = EcrTestConfigs.SecureHashKeyEcrWifi,
                MerchantId = "13593",
                TerminalId = "742001",
                Environment = EcrEnvironment.PROD,
                AutoInquireOnFailure = false,
            };

            var changed = original with { ResponseTimeout = TimeSpan.FromSeconds(60) };

            Assert.Equal(TimeSpan.FromSeconds(60), changed.ResponseTimeout);
            Assert.Equal(TimeSpan.FromSeconds(30), original.ResponseTimeout);
            Assert.Equal("TILL-1", changed.EcrId);
            Assert.Equal("840", changed.CurrencyCode);
            Assert.Equal(2, changed.MinorUnitDigits);
            Assert.Equal(9200, changed.Port);
            Assert.Equal(TimeSpan.FromSeconds(4), changed.ConnectTimeout);
            Assert.Equal(TimeSpan.FromSeconds(1), changed.ProbeTimeout);
            Assert.Equal(EcrTestConfigs.SecureHashKeyEcrWifi, changed.SecureHashKey);
            Assert.Equal("13593", changed.MerchantId);
            Assert.Equal("742001", changed.TerminalId);
            Assert.Equal(EcrEnvironment.PROD, changed.Environment);
            Assert.False(changed.AutoInquireOnFailure);
        }

        [Fact]
        public void WithValidatesToo()
        {
            var config = new EcrConfig();

            Assert.Throws<ArgumentException>(() => config with { Port = 0 });
            Assert.Throws<ArgumentException>(() => config with { MinorUnitDigits = 9 });
            Assert.Throws<ArgumentException>(() => config with { SecureHashKey = "abc" });
        }

        [Fact]
        public void WithKeepsAnAlreadyValidatedKeyWhenOtherFieldsChange()
        {
            var config = EcrTestConfigs.Lan with { Port = 9300 };

            Assert.True(config.SignsMessages);
            Assert.Equal(EcrTestConfigs.SecureHashKeyEcrWifi, config.SecureHashKey);
        }

        [Fact]
        public void ConfigsWithTheSameValuesAreEqual()
        {
            var one = new EcrConfig { EcrId = "A", SecureHashKey = EcrTestConfigs.SecureHashKeyEcrWifi };
            var other = new EcrConfig { EcrId = "A", SecureHashKey = EcrTestConfigs.SecureHashKeyEcrWifi };

            Assert.Equal(one, other);
            Assert.Equal(one.GetHashCode(), other.GetHashCode());
            Assert.NotEqual(one, one with { Port = 9101 });
        }

        [Fact]
        public void ToStringNeverPrintsTheKey()
        {
            var text = EcrTestConfigs.Lan.ToString();

            Assert.DoesNotContain(EcrTestConfigs.SecureHashKeyEcrWifi, text);
            Assert.Contains("secureHashKey=<redacted>", text);
            Assert.Contains("ecrId=ECR01", text);
            Assert.Contains("port=9100", text);
        }

        [Fact]
        public void ToStringSaysWhenThereIsNoKey()
        {
            Assert.Contains("secureHashKey=empty", new EcrConfig().ToString());
        }

        [Fact]
        public void ToStringNeverPrintsTheWebServiceKeyEither()
        {
            var text = EcrTestConfigs.WebService.ToString();

            Assert.DoesNotContain(EcrTestConfigs.SecureHashKeyWebService, text);
            Assert.Contains("environment=UAT", text);
            Assert.Contains("merchantId=13593", text);
            Assert.Contains("terminalId=742001", text);
        }

        [Fact]
        public void TheKeyDoesNotLeakThroughStringInterpolationOrFormatting()
        {
            var config = EcrTestConfigs.Lan;

            Assert.DoesNotContain(EcrTestConfigs.SecureHashKeyEcrWifi, $"{config}");
            Assert.DoesNotContain(EcrTestConfigs.SecureHashKeyEcrWifi, string.Format("{0}", config));
        }
    }
}
