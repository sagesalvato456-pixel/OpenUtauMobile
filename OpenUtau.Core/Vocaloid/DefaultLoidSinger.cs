// OpenUtau.Core/Vocaloid/DefaultLoidSinger.cs
// Lang: C# 10+, Target: .NET 8 / Android (OpenLoid-Flame)
//
// DefaultLoid: el cantante integrado de OpenLoid-Flame. No necesita archivos instalados:
// usa el motor Flame (VocaloidRenderer) con su propio perfil de voz.
//
//   Nombre: DefaultLoid · Edad: 16 · Personalidad: Default · Modo: NPC

using System;
using System.Collections.Generic;
using System.Text;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Vocaloid {

    public class DefaultLoidSinger : USinger {

        public const string SingerId = "OpenLoid.DefaultLoid";

        private static readonly Dictionary<string, string> localizedNames = new Dictionary<string, string>();
        private static readonly List<string> errors = new List<string>();
        private static readonly string[] searchTerms = { "DefaultLoid", "Default", "NPC", "Flame", "OpenLoid" };
        private byte[]? _avatar;

        public DefaultLoidSinger() {
            // sin esto el editor lo muestra como "[Missing] DefaultLoid"
            found = true;
            loaded = true;
        }

        /// <summary>Perfil acústico que usa el renderer para esta voz.</summary>
        internal FlameVoiceProfile Profile => FlameVoiceProfile.DefaultLoid;

        public override string Id => SingerId;
        public override string Name => "DefaultLoid";
        public override Dictionary<string, string> LocalizedNames => localizedNames;
        public override IList<string> SearchTerms => searchTerms;
        public override USingerType SingerType => USingerType.Vocaloid4;
        public override string BasePath => string.Empty;
        public override string Author => "OpenLoid-Flame";
        public override string Voice => "Flame";
        public override string Location => string.Empty;   // integrado: no se puede desinstalar
        public override string Web => "https://github.com/blakestorm-cmd/OpenLoid-Flame";
        public override string Version => "1.0";
        public override string OtherInfo => "Age: 16\nPersonality: Default\nMode: NPC";
        public override IList<string> Errors => errors;

        public override string Avatar => string.Empty;
        public override byte[] AvatarData => _avatar ??= Convert.FromBase64String(AvatarBase64);
        public override string Portrait => string.Empty;
        public override float PortraitOpacity => 0f;
        public override int PortraitHeight => 0;
        public override string Sample => string.Empty;
        public override Encoding TextFileEncoding => Encoding.UTF8;

        // Español por defecto; se cambia desde el selector de fonemizador de la pista.
        public override string DefaultPhonemizer => "OpenUtau.Core.Vocaloid.VocaloidSpanishPhonemizer";

        public override IList<USubbank> Subbanks => Array.Empty<USubbank>();
        public override IList<UOto> Otos => emptyOtos;

        public override bool TryGetOto(string phoneme, out UOto oto) {
            return VocaloidPhonemeSet.TryGetOto(phoneme, out oto);
        }

        public override Dictionary<string, UOto> GetSuggestions(string text, bool isAlias) {
            var result = new Dictionary<string, UOto>();
            foreach (string symbol in VocaloidPhonemeSet.AllSymbols) {
                if (!string.IsNullOrEmpty(text) && symbol.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (TryGetOto(symbol, out UOto oto)) result[symbol] = oto;
                if (result.Count >= 40) break;
            }
            return result;
        }

        private const string AvatarBase64 =
            "iVBORw0KGgoAAAANSUhEUgAAAIAAAACACAYAAADDPmHLAABM80lEQVR42q29a7CdZ3UmuN73u+59zpGE7DEW8QXZljHGcUiHiRPI" +
            "ICBjx91hUjMwJ0OmujpQCZk0driESVelUhPFMzVFOlPBFxIwFZKY7vTghMxMTFxuHEISdXowgQA2RrKMfJMvbclGl3PZl+/2vvPj" +
            "W8/Ssz/JR2KqT5VL8tE5e+/vvazLs571LCff/5fbu3dvsn///hbfuOmmm66p6/qGPM9/qOu6axPvr0qSZGeaZcvz2SxL01Scc+Kc" +
            "k6ZpxDknUUQS7yVJEmmaRsbjsbRtKzFG6bpOvPcizknXtpIkiXRdJ845SdNU6rqWGKOkaSohBEmSRJI0Fe+cNG1rv5PluTgRqapK" +
            "QgiCzyEi0nWd1HUtzjnx3kuSpiIxSp7nEkKQyWQi4/FYYoz9ZwpB8jQV533/Gs6Jd06m06k456RtW1leXpau6ySEsLhgeG7vxen/" +
            "p2kqXddJjFEkRhHnJMsymc1mkqapFEWBz9067zfzLDvZdd13R+Pxwfl8/mgI4aH777//EN5j79696f79+zsRid/XZn4/P7y6upp8" +
            "/vOf70RE3rl374WhLN+dl+X/OJvPfySGsOS9F++9tG0rRVGIc07m83n/cGkqIiLee5nP57YRzjkpRyPp2lZCCBJjlLppZGVlRSTG" +
            "/vdFJM0y6dpWsiyTuq5FRKQsy36RYpRRWdohCSH0m+CchK5bODxd10mqnwXfj7oQeZ5LVVVSFIVtpHPODmCWZfZ9770dDrxnXhT9" +
            "Z9Pvez0sIQQ7OIF+P8syERGZzWZSlKU4EWnaVtIkkbbrJM9z6dr+nuHw2oHsuolz7htJmv7JxsbG//3lL3/5+HCP/rMdgH379vnb" +
            "brstiki8ee/eS7o0fX+Spr9YluVrmrqWrr9dseu60HWd0y/JsszVTdPfMF2AGPsDGmOUtm3tFuOQtHoQkjSVxHup61qSJLHfiTHa" +
            "YWjaVoo8F58kUs3nkiSJeO/NgjjnbMOSJJE0TaWqKjsAMUYpdNOivid+B4cVtzvPc2maxjYWv++9lxCjpEkiXQji9PvinB0EHDC8" +
            "9mg0ktlsJlFEcj0ETdNIkiQSRcTrocPrN00T0zSVuqqiS5IYQ/Dee5dlmSRJIrPZ7D91bftH4v2nv/jFLz4vIm7fvn3utttuC+fa" +
            "2+R8bv0nP/nJEGN0X/3qVz8kSfJv0zz/6RDCSgihq/Q26sZ7EXFd1znvveOHxy3y3tsNzdVKdF0nmZp2cU4yPflt20pRluL197Ms" +
            "k5ZuhKfbGWKU1Hvxuoh4z1R/J8syszw4hGma2qESEbvxeD38fwhBGj3IsFJJkkhVVf1GwSKISFvXEvWmY0MTtYxdCCL6ul7dHw63" +
            "qIskvyF1VUnXv44LITifJC5JU59477z3McYY67oOWZZtL0ejt3rv//mVV13VHD58+Kv79++Pq6urycGDB+P/bwsAc/JPf/Inr+68" +
            "/7SIvE1NURtjTEIIrmkaW0w8WJok4tTEwVw7vRGdbr4TEZ8kCzeLzSUWKsYoo9FIptOpjEYjmUynkiaJrKysSFVVtnHee9uQVC1J" +
            "FJGUrAf+HZ8ny3OZz2a9349RvG4AfHiapmbqm7q2Q4GD4L2Xsizt53HAYImappE8z83i8AHDZ0JsYhZONwUHNs0yaXWNsUZJkiwc" +
            "zPF4HNM07bquS7Msk42Njb/rQvjlBx988PFzuYTkXJv/zne+87/NiuL/SpLkuqquYd+Tuq4dbljQYCaEIKPRSFLdVCwyzHCWZRYM" +
            "IthL1HRmaWomGjdG1C936sezLOtvkgaLvCitvkdKZrwoCmmaxr4/Go9lPp9Lnuf2u7ACeIa6aWzja7r1eVEIYpw0TRc2IerhKcry" +
            "jE1FPNFqYOqTRFo9GAgkYRHrurZnjDGKTxKpq8riF6xH27YWJItzEkNwdV37EEJs2zZkWXaFd+5/uPLKKw/fd999j21lCZKtNv/G" +
            "G2/8oMR4T9d1y2matm3bpnmWOWxIiFE6DfiiiPlv851dJ159KfwcbgKfeq83HosKv48MATe2qipJk0TyojBX0Lat+CSRTuOJJEks" +
            "tmiaRoQWD4vtvbeIHIsJn5uoKU+TRLy+73w+l7quxSeJuRYcMudc73ZilKARvb034gENOD25Cw4yIz07DllUCwS3iUPR0sH2zklB" +
            "h1lidF0fH7RJkqx4595z9VVXnfh/7rvvq690CM44AHv37k0feOCB7uabbvpwnud3NE0TQggxhJCw/xQRyQZpVZZlMipLacnE1WoJ" +
            "OO1ZWPAksXQMBwjuBEFaCEHyPDcLgoXjG4lDEvs3tsDPU7aBQxNj7H0fWSMRkSLPxXlvB4VdkSerhOdAFA+TjjSy00NZFIX9O4LE" +
            "pq6lKEsJ+js4RIm6zRBC//ohyHQ6lSzLzFrmeW6urlCLBEuLS5dlmYQQfIwxaGD7z3bv3r32hS984aGzHYJkePMfeOCB7p/dfPM/" +
            "T/P8023bdhKjc855+M4QgqRZZpsBf2cbqLcIN1FEZDQaLfgv/sIN5JQLPhz5NlxDluf9oVJT2Wh03ratLQSCRdH8H+mofX69pYj4" +
            "4b/twOiBglnGouKg6wLb4uOG4gbDIuDz42IILFia9u5E/w2WrtWgF5hDmqZ9hqOZUJpl4ijWQmZSVZVZFFwExUlcURQSYwx5lv3T" +
            "3bt3H77vvvu+PTwEHAR6EQlvf/vbf2Rpaek/OpGi6zpxIi5SCoMHw03BjYK5xgYG+Hhd0EZ9K4LAXEGamQZhAHh4cUejkTg98U1d" +
            "G0jjnBN9uAUT2batzOfzPnNQkCYvioWYYl5VkqWpZHku3jmp6trybsYqqrqWoIvKKRkOOn4mpaCSrZkFxGkqs9nMDmymlhA3uFZT" +
            "jk00y6IgGALDqGuGw45Dx5YBhyjLMkkVVPLeRwXI5k3T7P3yl7/8dew1WwCnuWNx7MUX7xeRH6iqKnjvfaanrG1bGY1G/c0gBKvB" +
            "TVXfmPT5am86FaBJ6Ea0TWM3CpsszhkS11F8IM71eTUdMgBAuJkc3dsh1NdCQJhlmTRNc/omaZrWNI34JOkPqaZ12Aih18eBFAVq" +
            "opp6yxD0PWHWOZ+He/HeS57nFoCa29Cfw0HGs3Vtay6B00N8PriugoJCHBARkSRNcYCcWoE8xPhjP/ETP/HHjzzySMe3Xvbt2+dE" +
            "JHz9H/7hoz5Jrq+qqvXeJzAnuOVI19Is698AMGySmCmN+r0sTSVLU2na1uBSBF/YDBGRtuukriqZTafSUURf6M0NIfSvCYAH5rLr" +
            "+uAMlkU3NMuyfvNhtgl+LoqiD+7075kCSjjg+Oq6zhC7pmnsYKRpaoe3Ix+O12F4GqkcTDWsAn4OFsmrRYGLnM/nUlWVpZ6Inxgz" +
            "wJ9pmoon8EtvvMQYZTqdGgDnkyQJMbajsvzBY8eOfVREgu65OKB8q6urr51sbj5a1/XI9UfOjl2e530UTAgY4/NA9fKikKgHwWmK" +
            "gwjZTFWSyHw2W8AOWg2SYGoBCKX6cAy+1E1jZhexAaJzuKUYYw/L6kI6rRGMRyNbXLwXxx4w73hWuBlG6mCeg97Yjv60268b3DaN" +
            "QboxhP5zUtqHTUVsgLWEi7RagUh/2Si24gwpy/PeeurPw2WkaSqtumHvfdSDNmna9vovfelLz+zbt8/5gwcPOhGJ66dO3ZKm6VKS" +
            "JCGE4LBhcYBr4wbg1CLPzvNcyqIQrzFAWZYSYpRIgZTGFL2JJ1wb5otTp6iLlCo+kKifTsjsAnJN6Wbgv7qq+oWg7zMWgfdFwFlp" +
            "vg3giX+upZSNswC4CqF14bVK1eR7zQBgSXK1PrCaSOtgHTMKQmGFuL6AS4GgdT6bWQCOS2lglz5fF4Lz3ndpli0XWXaLiMSDBw/2" +
            "P/mOG254dRyNDoxGo53OuRhC8C35ujRNZa5YOwMdBoJQYSNqAQeARkugTdu2Mh6PzXowGuacW0DjcMprDf74wR0hdri9s/lcyjzv" +
            "fa/3hrWXRWGvgwWHCc3Ulc3IRSFgbZpGRuNx74sp8IM1bNtWyrKU+XwuZVma++D3wPvywUV80KiVGY/Hdvic92b2+UJYnQC1EAWq" +
            "4HI5bsIaYb+SJLG/i3PB90De8ZUkue7ev/zLY15EJJblu4qiuKDrulBVlccGI+qv6/p00YbAEYM4ddMNddPDgNuL046TjtfEqW01" +
            "4EFhRAg8MhiZvs8Hr23bPpPIMnFqiieTSY/8leVpOFd/n8GVtm1twRHowQenGkgGjTsYvMFBgP+HW4F1QKkZGEiuRRsralE1cTab" +
            "2YGrFdrGYUdAyEAQX76u68RTPQNl7BhjfygVVMMepEnivfehKMsLvzeZ/HcWBJZl+e48zyNySTxMqdCmVej0FGIRZ7NZf3L1NmVZ" +
            "Zlg2bgNMWEbYgRCaJrRwovUBbDoCN6FikqWiekCQduL7ItKXl/X1ALEmaqWwaTis+DPG2KeGSSJLS0v2meBOHAo6avYdVTeHmAA2" +
            "MdFDNijj9u+nASCsJ9wmXBUCU7Z2RVGYW8b3cYCRXpdl2Zt8dVM4dHgWtWYxybJ3iYi4t7/97ZcXef4dcW45hBAlRgeAAWZyGPFi" +
            "oeFTLXdVn8xwa6XYO8y1o5SHb3rXdT04Q2YU7+u8l9B1UpalTKfTBdeE6B/WCD6dgREcHgROcBtYYJRzFVe3A8JpF26gZQpa2fN0" +
            "MBjkQrDHCCgHeFwLwWeBpcR/CG7hDnFQheKoyeamuUheMyCizGVQdxK1UjsJMV7nU+f+KxFZdv3vONwknDIu3hjaBRhRI9ighYuW" +
            "bmEIwWr0XMt3VOteyIuRIpEZTdNU0iyziBYZSZ7nC6YaBwxmHb4VlotTPGwk3BNK0XguQMDsgmAFASGnaWoZChbYikpqKbFZHNgC" +
            "wMGNZ9QSZjqS9WsHlVa2ICEE8c71OIoeEqyFV3eK9yzLUqq6llkfLLqu66Jzbqkoirf6tCjerB++Qw0baBM/FCDVuq7Fed+jdBr9" +
            "FnqiObVxzvX1fvJp8/ncNg6HAKe10E0UtSY4cC2hXhxcWmxBgREWvhtAqtj4huoSVVXZ4uDz4X2aurab1FAQOx6PzUUhbWvbVhJs" +
            "XowyGo8lIzea5bl0ejjH47Ft1nw+l1YxEliYRqlu7L6YQ5Dnubm8+XzeP1+WSauRP1saVAtxCNIkkTRJ+mfwPuR5LqFtf8y3TfN6" +
            "XRjPuT0iWNS4K41aAWpUSlawwIxyVvhV0XwcHwwBzWg0MlONqpYFdmQxOO2qqqrffDLnjlg3ZprbVuqmkdlsJstLSwtBKQ4RZyDT" +
            "6VQq3XAcYjaxWHT8LptyBJgJwbjVfC6tWjBUOXEbcWGwRvgZmP66aczaBb0IiffSoPyrh1pE+tJzCD2ySlVQpLTCRBS1UJlazizL" +
            "nFZK35BcdeWV/4v3fqeaHVfQhmWD6JXhSy5fIlhjX89+EQUYmD6JUVqFlN0AD+CDgn+77LLLRERkbW1NJpOJ+UQ+IPhZBnX4IA0L" +
            "Nlw2zgaAk7k+LQszkwmuIZBLw/dKAprsWXWzcUgRG+AmY53quu4DuK4zGBt1C7xv3TSn0VEKntla4u9wfShfwzL60zGB67quSZ1z" +
            "22B2uArHBQ1GpGBaI3HYOKDhDwCoUvSNQZ+KIpLo7WVeYJqmPZJINfWiKOS2226TvCjk4W99S556+ml57OBBeeaZZ2QymViOC5eU" +
            "KnMXhFLg47h5XIPn+MGCwzSVVi0Y594WCGqWgwg+EgKHmEOcE+k6qZvGUtEFmJiAGj5ErRaLzHcXRU8aobhFnJNG+RJ8qJHFnI2z" +
            "iFiNmVl6MHekSZJsV4TKIYBqqGCDfJOrTiIiDoGcmq8uBHGERAG3RxYQY+zzXI204TrY36M4A7pYovXzZ44ckRtuuEHe/o53yNvV" +
            "h544cUK+/cgj8uSTT8qjjz4qzz77rMxmM0m0Ds+kEmbWROUxAKK1TVBTHti9aC0EBwgIJT5nqVwFvjCj8fg0s0dTUFgcADqgfMOS" +
            "BI0j8H4I8Fo6HMYS8l4a2sigBwPlcqc4A9wBs6JwAZMkccq23pZcccUV/xs47cNcFmXCXHPLiNNNQRBoX2C9Il0DlRvFEPhbMFjC" +
            "IG9tu068wrqB3MPa2pq87ppr5NprrzXrlGiufsWVV8qb3vQmufGmm+Stb32rPHbokJw8efIMVAybhCgcG94pEBWJ2dQQJWw+n0tC" +
            "ET9uIXwz+/mo6arEKOVoZOaX6VzYBABpjujyNW0SmEyOiKIeJFQN7tqmkXI0WqCRdW1r6waLiliO3TilmomHr0Wg4cjktG0r4r0E" +
            "XZxEbycsgtGiwbvPMjO5KGmC9258e90MBFfICIo8t+KLmTvdxJdeemmBfMKBGB70sssuk7e8+c2ysb5u9KwEKKDm9ktLS/Y6QB07" +
            "4uphofBsSPewFviPWb0oW6dKY/eKfTAuwZG9iMjG5qZlEICBS3VFuBRlUchIL5LFHnDD6kYt6NZ1xkFA1gbXxujnQtqZ55JiAQFD" +
            "CpE/8jzvoVBlp8zxQTQdxN9TIEwEnsC3FUVh1CZuzJhXlaWICJKQ03Jdezwey4EDB6Spa/Nx7EONb+e9bN+xwwotTqtxDD6ZVdCK" +
            "4wKphA6+1fMJyWPWM4M3cCFVXff0dd3wlsq+Q1DIU4cTs5e80trRXxAokAWpBNkX/o66R6EXCpsLs49D4b2X6XRqzCK4ED+s9jEb" +
            "B4s9m81kczI5bbrIf3vQoIgta7dTOYHIJlDmbdvWSpve+56Ng2qh/jt+p+s6Of7yyzKnAzr8wmd//etfL6/auVOms5l1GjE6aBAz" +
            "fofIFQucBtDGiO5mG6mHx/gMutFBkUy8Hmchjfr1rut6VpAijY6smffeNgcuBZuI0rpQPSOhxheghdy7gOi/pXpOURR2UFqNdTxv" +
            "HPPkEInitGVEvBSRnmql/go/Vyt5wtBEJXOA2WI4NlG+a81jF1i2yoODq9icTuXIkSPn7GC66KKLpCwKM+9ctmZKOjYOVUBsLFfx" +
            "uLMopVvt9JYzuseBJyN2VVXJxsaGFaIWYFoCyGApMyXZYA9a4jwyJwK9jF3XSTWf90QXtVAW8OnzgmfBlDum3fnIGL9uJqJUp4cD" +
            "QRHqzdPp1FqghuYQ5d6F20ML37WtOIWBW6JyIw3jvB6fZbK5Kd/97ncXyq1n+yrLUi5/7WvtQZHNpNzUSRgGkE1sLEwnQ7w4RI1G" +
            "6ugBQNMLKGkInDPasCzLZGVlZQHTiF1n/EZE61bYwaUBhKzv7SiYxcHAwVteWVmAvPE5kPXgGZEh4fICdfSATTlqtOYELUhwRoBA" +
            "D6ecacuNLqZV0TROwMnHgnEblKNbFwe9A8Z/E5ETJ06cURo+W+HliiuusBoB1+kT4hI01LzKiB5vxgKIhOBzQChBbMB5PkiwLRXO" +
            "QgjSgRuot5FZVXjeSFA6gr66rhc4EkIgVgjBsqZIEHqn7iaoJYaVtxRdLYv3XtLRaCSTycQ2Kw4o0l45elaFwvf1Z9lfMs0bN61R" +
            "v48Fdxo4BoJLueky0Vtoi6CH7umnn14Am4YHAZ/vwgsvXICGzQVozo9Dh5vOfD28NvgE3PgZQ+h7/qgNDQGckCuAT4ef77pO0iwT" +
            "H6NEwustINZDwmAa9zigg4gBHu5ZrLTeb2tC5XluRRsWlLA/HqbBeO8UxKHfDcRILuV6tRR1XYvXxeImDfNZyoOHScy1bBypFMym" +
            "mauOTPQ88swz1ir+SoGgiMiePXtkx44dFvjhM831OTvCEhLiITJJhKuLOUw9WZohXtIhHdZ/S5JEEu7hQ1lbXQfqCgCfyrK0g2jA" +
            "z6AZlq0dnglZjDW8qIuF+waXAs0q/Kxm/YD9c32/ITi4aRojSICehYfEhyn1NcD2xc0Yj8c9NYvgUjaLViLWm8gppJ1y/WyT6VSO" +
            "Hz9un+WVvi6//HIpylJOnjhhBAtrL1OrlRMhBM9jPftIfXVDM8ILWu7iocsQtOUba+Cd6/ETKlThfabTqezYsUPm87kRYoHhwzKU" +
            "Zdk3zmopPFKJuWkayZXdjMIQiDsiIi2eqa4lUKudIySR+zxSLALzyGazmREkED3C3+PnGWOGCS+0UxY5/Xw+7/2ncyL6ekOMnYNA" +
            "xA3ouOUGzOlkIo8fOiSXXXbZWV0AvoqikFdfdJG8/NJL9hpIKxG4zjVGiOqGDOAiSjszhfB3pFII+oy1RB08cBteTTFiKueczKtK" +
            "fu1f/Sv58R//cXn22Wfl6IsvytramqytrcmxY8fk6NGjcuLECdnc3JSubY3ehfXG56mqSiKBPghk0TIPIi5iBrg8oLcJffYUZh59" +
            "eHYLaVHxYDBNQi1VSZJI6DqpiYVTVdUCXx8fvNV4AKgVk0L5phimXlUyn89laWlJ1k6dkqPHji34+7MFglmWyTXXXCPfefRRa1hl" +
            "KNoiYW3kMNq0Hr4iz2Wqqibg/bH/RN/CSLOLpmkkeC9RnxeHGJhH1IBsXlXyrne9S37qp35KRESuu+46ue666xaewbiBdS0vPP+8" +
            "/OZv/qbJ27DbEDL7TBgBE7maTE53LAFYU0uKaqNxPZnoiJuOX3DU8s0FHkemGoclo5ucUYGIg5YC0KYCJ8yF95SrRnI3cBej8Vie" +
            "PXJkIRgafuFgXHTRRb31UrCHMxL8bqW3udXsBTe/qqo+hyaWDaxVWZamZ+SozYyxBaRjVuTKMtnc3JQ9e/bIL/zCL5yRxnLnEHiT" +
            "4/FYvvKVr8j6+vpCbwLAOcDBjsApfMbZbGZ1ihCC5OpyC40xarW0gOk957D8gRwFLSAp5tpPZxEyfKTGAqydIxRsDFMnT2TPITaP" +
            "lKuaz+3GIt069PjjZ6h8DK2AiMhll1/eL5YWWRDjlFqaRetU27ZGW+PSaU4NoFhIkDRRd0ffIw56rRVCcwN0qbIsk1/54AcX4hxm" +
            "ILGrExE5fPiw3H333SYSwcH4kAtgqJ/uSU6dS6CYcVCPimeapj1GAeED+HYEhczBR64LEGE+n8tsNjsNPqhZ7NQfmUgTvTlHoPwn" +
            "cAZYH7zPaDRaoI1lWSbra2uyvr7+ynInegCuuuqq07x6egYsJnw+p6etMolw4OG7kS4aUUZNKg62dQABtlULV9e1NE0j62tr8ku/" +
            "9EtyzTXXLBBKzma9wLS66847ZXNjQ4qyNFfp6BmR7uVavQTrmWFtVi5BcQwUdBy4tbU18XmW9d20isl3BN02hHfjxWr1SVyuBFiT" +
            "kA6A0bUHHH6kMJbuUArIJjqSkhber6oqefKJJ14xDsDXaDSSH/iBH7AWMaSxCVmuruvMPFpaNyDBWo1EO4Gd9yJqDeJAcyDE2Efz" +
            "ItI2jSwvL8tsNpMbb7pJ/puf+ZmFjp2tDsAf/MEfyCPf/raMl5YsA5rP55Ir4wr4APYHSGet/RbtoPcQ65QMQKu2afp977pOZmpq" +
            "OzWJnW5URny4ha5XRcXYHIGbzro5zL7hQAfVL0CdIFiIFpsQSYM/DzpT27bywgsvbBkIIpi96qqrej+nViYnHgJYvyjoJISX43Cg" +
            "qRQspVFZWtNpmqZ2eEDfho/F86+vr8vFu3bJLbfeuoD5n+0LluGhhx6SP733Xlkaj219ERjPq8oKXBVZMqCraD4ZlvOte4o1nNCa" +
            "F4Kk8FGcpy8EeLoRMJFFURiYYoIL5NdwI5j3j4ZPnGDIuqAq6EmJQ5R67QbqF9j0F1988RUhYSZdXHDhhVI3jSx7L6KmmwkiDFYV" +
            "Kjlj7VRp2rObYpQEaRUaNtDRREUV0eIL8ALEHB/60Idk586d5zT93nt5+eWX5ROf+ISxlFmPARew0vZ2A42wzoQwVnVtmok4EA3p" +
            "BnR0Ma0aOFTYwCKyZNny0pLlmYxYObr9w0YKnE62IjgQRZ5bhRGpI3Pj2BRbBTLP5YknnrA8fKtA8LWvfW2vZ0BSLp50AxFXAETB" +
            "oiOtRb28aRo75Og+XkAzlUwCQCvNc1lbX5ef/dmflR/90R895+bjgn3yk5+UI0eO9J+HSB7YRFwUWBpYKQSl0FYcDcAvUZwCl7wl" +
            "2L2vMyhNe0TtVFyOrVUpI1FT6KmOv9Atq1aio9SpKApptHePaWCQRAVnjQmT3PfHtXiwjl88enRLSJgDwdFoJEHLskNmLg5a0I3k" +
            "OMQ6gzT1A/jiiIWbaok8oSppjFE21tfljT/0Q/Ivfv7nzxB3eCXT/xd/8RfyVw8+KBfs3NlfJFVNQ98js53rupaMYhkARYjDwMBC" +
            "4AtW0XQ67VlS0FUEWQTVQCiAIKq0SJ1AGy5U+EF5FZg/cn0sSE1pJsw/t5pBaBJ+P9cOX9YO6qhRpWtb4wZsFQhu37FDlldW7FlY" +
            "NZTjAVFufasw7gLjSFk0rGFoegNEnC3yXBK1Ytu2bZNf/ehH7T3O5aqeevJJ+YNPf1rSNO03Scu/sILcn4lL0YAKrgEqC1YBYubS" +
            "MRBSNIri8zdNIz6EYDcUUChEj4eKW7AIWFRm9KanGadWMw8hSAlZGUqtmAfA/WtQ04SODkycJxdS1fWWBwBuo9RAsKoqqwkwYAPz" +
            "2+r7cI2fsQbGK4Qp8Kr9g0XPtK/xl//lv5Tdu3ef0/QjIL79jjtkMp3K8sqKKYUBpWOUj+s1XC1M9fJxgIc4Dq8BMinEr5E56D4R" +
            "7XjQ7MmATSDyAurs8NscWKExIujiZVkmpXL9uFSKlMQRvxC9Amz+uZtWRGQ2mcjR8wgEURhifiPTx9AxhDgGgSqDWQzYLHAm1BI5" +
            "52SmtPcTJ07I2972NrnpppssG9oq5fPeyz1//Mfy7Ycflp07dxpVHs8KYQluxAHuj0CzUcs4m07tkATlA8AFgPpVq0tjCxxCEI88" +
            "G8BOMhBw5GgXtxDCiUPJUm4gYfPdEXkSHMOZIlvWA4c4A/6WiJ8IXuCjn3jiibN27A7jgEsuucSaRJnNa+XTQZMGq5XFQamZ1USQ" +
            "OeF3NzY25PLLL5cPfOADZ/D3Xsnv/+M//qP82Z/9mZSjkWxsbCwcPjTGiII3rKoCXmaWZSIUU9nzacCYUPYFF4A6hiOJOg+AAhWn" +
            "Rk3g8vLywm2yfFP9HQoh3HbVEW8NJ20oD2tdLEQ1A127qipbXCwAt6g1yoU/cuSI5cJbBYJXX331GcwfzkQ4j+Z+PTB7uFBmBE49" +
            "OIhJ8HMf/shHZGXbti0DP1yqtVOn5PaPf3zBtTKNnPv84BZMiFK5g7jpC5pEegiQuYATyCk1S80658QbNYhakpxzMlXYEGVebMp0" +
            "OjWgiOlfXCdIVM6Vy8NwC+D5oUKHw8X1+URPOzNfsPiIeI+99NI5SaIXXHDBQhcO6wMPG2GN/KquAlIwCMBq7eYVKlyFEOTUqVPy" +
            "3ve9T974xjeeV8onMcrdd98tTz/9tGVeQ5fYqQIacne2xJEUTHFw8NrD/TKKmx4Q6C2iwhhjFM/iAwubxbQh8p1D1SzrpKXb17at" +
            "1FW10NptkzGogYHlYoCgyeC92AfDT25OJvL444+fMxPI81wuu+wyiyPg+/D5WH3DhBzUpLIqGjIUUNqQDm9sbMgNN9wg73nPe847" +
            "5fvigw/Kv//iF2VlZWWhnR4FJ+sY0jQTF5J1Bph7UJGszNLSUt8voB3QZ2M2Z1nWHzzQzTmyZToWNrMoil4RnMqcoCEzIQERsVA7" +
            "VKDiEFe+uErmNJUpqE8/KN0ZNHVUtDL10U1dy7GjR7d0AbBMe66+2gSdFopaqqHDfDwcABwWmN5O5xYACFNJdrnwwgvlf/61Xztd" +
            "sDlHyvfMM8/I7//e7/XCVcQxsPcjsixrB7P4Iy4DOI7ovh5CvyCDAFJHNgSACc08njtlWREEUmlG5lCfBHiSNXLBGbAmUC2K4EMH" +
            "6j7mqB4BU6KROKBYnPaEWMFAvFp93+eff35LbgC+dl18sSwtLS3UyxuqPFrVTEfX4HM7SqmKsrSuXRzIpm3llltvlde85jXnlfK1" +
            "bSufuOuunu2jvY9Mru2oUGaBqvZd4gBznwPio/l83qOT6rog3gUrK4SBNE1jWgVmlTgy5vwQ0WNOev9cTWIwAc2NzJfzBCsDH3Ak" +
            "6sREEJZRRYqDg1cqro1qZV1Vsry8LE8//bSZv7O5AWzIVXv2SJIkMp1OT6trKhuI5eG4JbzSwkukLiBcAOecnDh+XN750z8tb3vb" +
            "27bcfDbZ/+e/+3fy1Ycekm3btlmzhl0EFe4d9vA5aj5pmmZh7AzccEb9mJ4sBmYXsZI6l+pNAZ1VwNu2lY2NDVsUNk05ycAwQ8XM" +
            "FunhWOStZUf4TjRsVlVlnHUwUzY3N3v5GC0aAblis5brw4YQ5MSJE7K2tnbOQPDVr361jMfj0yif4vksyGRUNAW/UlUi4YFTMM2b" +
            "m5vyg9dfL7fceuuW3ET2+9/61rfkc5/7nKxs22YHcZhyA2ZGu1qFgweNH82QuGRu2oK6xrgQnKpiXxPVakCmg8PskduiWxemA80V" +
            "kSjcnKogDbSUTU0jNIBYiADIIDB5B24hgkYQUHiWAKBnfUjT4lPLMZ/P5Ynz5AZcdvnl0qqsHcCQlgIjRvcg/gBTORqNTnf96AH8" +
            "8Ic+ZKjaVimfc042NjbkE3fdtcCesj7BprGLYEAO9AZIpxlBYU4MbrCnuEOLKfl8wDE3IaHailmBmWLHDd00cAEsQtYoviHio/lp" +
            "zVkjadl6YqE40rdFbg8dAHMnND8oL4oetaLGDvTEIwDEAX3++efPiyT62ssvN99owafGJyyJExUQYx0hrn1sbm7K+973Prnm9a8/" +
            "Q0fhlQ7Apz71KXn88cfN8mU8qEr/tNZ7iD6oVYI6O8cRC6NqBtoH3HY/lLupSVVkQdMYEWxG6tpBCyQm6UaNivCLkX3coHO1C0Hm" +
            "0MqhxkqUJ5H7Iw7I89wIDaK/Y36XRB2wiYiQn3322S2jbyzcxRdf3OsY44Dq73cU2CLwgwszJpRu0ssvvyxvectb5L9fXT3n5sP0" +
            "/+3f/q3c/5d/2UvKamNKVGauNZUox4BTNQbmuKl0PpuZG2AdQ349WBIcdh65AxzG1N67TjyUJs1X60KsrKycnnilJywOhJ4K1bDp" +
            "SMoETJ+yKCwCZSjV0SEYYtx1Xct0OjUKMzR8orZntW1rvjHxXp566qnTLW1bcQN275bl5eXTbetkalsdUumckw7DnLR8ytIur3nN" +
            "a+SWW29daCzdavNfeOEFufOOOyz1NRq8WkrmAvDYOaSjDAZhUzFkguFcyN3Cwi4pbwPu1kS/lc4HFZTE+AA0ZgV+FzcD+Tqo0Ozz" +
            "OI9FxD+ZTIx4ARmXjrpWcRpxq7ksDHLpWCXkRmW5MEMHrVIMUH3ve9+zoGorSHj37t0LBE8MimyapufS0ahapFqmQ6ixw4c/8hHZ" +
            "tWvXeaF9bdvK7R//uBw7dszYO0g3AS3XdDM9Iams0F4UhVUyGb1EdF9TKT1qIQ70vqWlpdMM67oWB1VytarW4BOpYZBHlzD+b+Va" +
            "xc6RkiCaRwURHxYEzo2NjQUWMCBUBpOYhJIkiXittCGNaQHIKPjB6GM1n58XN2A8HsvFF19sswS4kSLX2YG4jSB/wmKtr6/Lu9/9" +
            "bnnzm998XilfkiTyp/feK//wta/JBdSoiliLafOmtE4CUZDM4f5FUw5F2zyprsE1pzQTyXQd0tT4HNaPMGgA8g5BnPrY+XxuPf5C" +
            "eb0j+HGonIFAjcfBMtZuv6ONCTitiAnwMBA8gI8HCweRe6WEUaB8s9lMnnrqqXMGgnmey9Wve52sb25KobgCyq0c23iNQYL++6mT" +
            "J+Xaa6+V/+mXf9mg4HP5/e985zvyh3/4h7KyvGwDK7gZBO7QUmXNBtBtjEsBwQ3wFdqu61NmRWcb4lgOB1/gQmJfutNqIGfQ8z23" +
            "dhuIQ63egQSJWAKVc+i26yQQBIygcGV5+fRpw4FQF2BVMEz7oJaxVnvbuTfAmkwUB8D3zsUNwOfdtWuXaQdyi7mJRWsjJmv1j5eW" +
            "5Fc/+tH+Gc4j5ZtOp3LH7befIVGLaapjfZ7h7+G5eQorZiFCXgYj6byCbFwp5Ooes4esN4Kk5DnDMkII2qCGVbAImVX9Ow9K5BFv" +
            "hapdsCq14dBKUoA2DSZmIMpn5mqkIUosjAzyZpIk4qgNanl5WZ56+ml74K0Cwd27d8u2bduMzIngS0R6wEfJFTjcJ0+elPe///1y" +
            "9dVXn5fpd87J3XffLYcOHeoFtYhhDIgWwg0liUcC2LIWPbjZurbfbWleA/ozeRim1XHU5LOYBNbahLuoYdUIIUCJgA4VpDhdq2li" +
            "NVG8CKtsMqU8z/MFESPObT2JUZtf05QsAwcAqqGk7QNJk/mgle3pp55aKESdiyU8JLN2NNMAgs6bm5vyX99443k1dGBT/v4//Af5" +
            "wn33yXg8NvSt08mpQiVni9wVEOPeCFw6DgIhum2t57pWkPQHR8KIPZq9xBhlrkOkkGnZqLq6ltl02lsaHroA3w0qkaMmD5M80Ydg" +
            "4if8Z1GWVhlsBgUjk1vTMW2s0hWpT79TdTH4epswxnNwQPPWh3npPLgBKysrsmPHDuvfN7EGOhCgTl9yySXy4Q9/+Az28ytt/rFj" +
            "x+SOO+6wSWUQjXLOyWQ2M2bvkHENWVweCMEKabX2Y8Ki4hICqDJ1E4LVx+Px4pBLvbSeB3JRb6QH9Rgmx36Z0jyMVcVmoRCBU46b" +
            "PtU0kLXqZIC715jfp7d9AS4eoFQoFfPsIVgjHrh8+PDhVxSQYnLnlVddtYAzILdmEGw2m8mv/MqvWEPHVn4fG3LXnXfKi0ePGmK5" +
            "oDpOXUOwoMjLQY/zKuPOsrOeajQm9EzVQhR2BtNCFyq6nhDBSLCw9W+EIB4pHUggABqYoIjNQWUOCJaNQVWSBPfPA1hCzooJXjBj" +
            "bMbtQFDgiRsC0MYrnMkdspgP9JLqBmxlpp1zsnv3bgtmAZ/OFF3L81yOHz8u7373u+WGH/ux8075/vzP/1z++q//WpbGY5OVZc3B" +
            "QNxKtjI88dyRRrBNStc8HxAuC0oCMeXWNrgWHtjBxSPmecCqjcqydwGeTkVKvfGQcOX8n/vKMIhgOB0soVp6XdcyV8JpVMo3t0Lz" +
            "XBu4obqurUfAaZaRabmYkcM8z2U0Gsnhw4cXXueV4oDX7Npl2sQNRcMiIpPJRP7LN71JfuEXf/G82T2HDh2SP/zMZ2Rl2zaLtmuq" +
            "tgHBxMYbH0EZSbnqATCrCmtoG62Hg90KhCCQOcxV0KIcSMs6UjttyR1jyouI9EEg6s+FTvjIqP8MESkKE6j1s2xJkqYmXoB+gI4w" +
            "aUwEc4QVQIy5I3ADDSFMB8cw6jDIQlgq7dlnn7Vuoa0ygatf9zo7AMYPVAS0LEv5yEc+ck52D/MY77rzTpN1h3RrQgEmLGhVVRYT" +
            "dSFYkD1s4OCYChkZbiuY2DyQA6BW1IJdo3qGPKsAFq7T3sdIot4RUrHD+j8oRObLiGJlub8uIt+mJEkWdAStPoCiBGn0C00a6zQN" +
            "q7WBodTx6lZBJKQQ5hGfJS8KOXXq1HlxA7Zt3y7LysXj+UOz6VRuufVWee3u3edV5fPey6c//Wl5+JFHZGlpyQgbRVGYqxuNxybm" +
            "lGjgi8Fa0ELgUXZY4wyxFuRcSBMAmIWjWQuoGnrvJaWAm4tLeZ4bwymS8MXGxkaPA0BPFinhQnMoxrJQ1M/cAIA7KCfPZjPbJKR8" +
            "PJEDShvIWzF5E9lHpJvJLeItsWAQRYOzWDeNPH7o0JaQcNRB1ldffbURL/M8l2PHjsnNN98sN99883lX+R76ylfk3s99zlrEO9Lw" +
            "MdqconvT6dRocRxXAHW0GIW6gKwHQ6Fw4BaQiWfMo65raYlrgMtnhSDtvUwGfA6M7vEAQnDSkJ+DVmw3XwEd5rFneW4nFvV90Lo7" +
            "DdzyPF8YJMnWxtG8IZ6+AZVLR7KxNQYi0NBK55xkSSLTyUSOaGl4K0jYey+XXnKJPdPm5qZcdumlcusHP3hGv+Mr3fzjx4/L7378" +
            "4wvS61777826YWqKCkEGqrOgG4m1hVgeHxvsWFsYVDWFwnluAiByo3prfGQtcKQshnoMaw6nWHCdJmULPpvNDB5N0lRSEQMZEIQN" +
            "+WY80j10nTToENZDxfMAUTrmoUzsXjLlB4qe3kZENjc2zujemelthm7AOUmiu3YtROK/8Ru/Idu1oWPLKl8IEkTk93//9+WE6hXa" +
            "3EESwgARpqoqE8hkckpUxVBmXsOKhEGQDFmbVBtEjbWtDTw+SWRFtYIrlZQFEMcSuDKQ5ecRuR7p3HDuTKC6tWkAE/7PaNowxRAS" +
            "hJxOp9ap0lIvwcLN0iyDpWBxa8B5K4qiH0NHbdIs+XroscdOj4XZgiR67RveINuUm/cvfv7n5Qevv/78iJ1JIvfff7/8+wcekFIB" +
            "LyPTIqgDpU0lbzNNCxHPIJCLMcqmDo3A4S/L0jCJlBpjWLwDBF28NwZamS6hXlxE/QgW0TGMrIknjnobNIATrD4XEycMiSKZMp5r" +
            "y1IqoFp11EPAZUhHTZbYoqDjWhhUwmuzuATrCTXUMYMTf+zYMZlsAQmzBfDeyw//8A/Lz/3cz513ynf48GG5+1OfMo4Davg4rAjM" +
            "bESbIm2RhkJ6FWrGDQTCCgh3ppelqiqpaNM4EMbrMI8iUdeCPcKlwEFlZVMTyEDjK99gK+eCoEBzcnBDWsLPZaBc1TaNbE6n5ktZ" +
            "kcqmgqEzaKA7gE1HqoTCUkLNJyzGxAJNeP3zEZBK01Te+MY3ygduuWXBhZ0r5bvj9ttlOp1KrpXBxHuZTCYmP4tAEHIssGgtSCd6" +
            "KxNtr7MKLKF7uGAxRhmPx6fRVPosrGHEfRaOUE9E+8ByeGinKbEhHY/URNjR5FBWomR9emDU6DDJWQNQgxvesDzPbVAUgjtw40BC" +
            "ASTLxSOOZIEPzGYzmU4mhpVDXQQu4Vy6AThsv/rRj8qePXvOGTPA2v2bz35WHn744b7TmBpIkVJCRAIzhsBxMGQUuoLaM7nQoYxJ" +
            "YOpiYdFwiCJVPxHooVsJRbBIwy6wHkyrX4CgqSbRTx5VDr6QhDnfFh5+DHHEVqtzgdgqgYYux8GYlo5gULShs95+p1rAdqI1eDQT" +
            "SRw+FmxAeTPLMpnOZvKCsoS3Munee9m2bdt5c/q/9rWvyec+97m+u4h4jE3TSETzBSZ2UYkZaeaCrLxG9AxG4ZZmukHOOamUNjac" +
            "KsLCldzkGgfyOzYxnS41ZwbYp14TGkQC1cNHmrWysnK66YAW3IAFpX1x51BV15KqGAEWYjqdnh5potDobDazjZxMJqcncEN5ZDA8" +
            "Yqg2WjeNpGji1Apikefy5JNPLsi7novAca6bf+rUKfnXv/3bMpvPZfv27b3pV2q6qZpSUOqdk6woTBaHx9Em3PI1UO8Ena4miJb5" +
            "/VxBdKSa4ofDqEiYyxTK1ZLMif7vFZhynAZCf4cl3ZHejUYja/2CD+JWcuu3RwMFIXXssyJhDMORrvBTZ9QVQBjRQLPV+oMpi+gp" +
            "L8tSnnvuOZnNZrKysrJlIHiuzcftv+vOO+WFF16Q7du3G1UN5FRLRTGhQ6eponJakwQ9VL4WMihiOaE0G9FMQ8O2ztbxzApgsMwI" +
            "OBtqaZ8qKIeYAtaVx/GkBfkkbI71Ag5au4ezc0dlKU6pS8vLy4vEBrplMNXDMWod1aoBlTIkzYhXSrpBxozRzzIqS9nY2JCqquTo" +
            "iy+e8wCcq3KYJIk88MAD8ldf+pLsvOACqRRrsLoFup41A4CQEyxpDeIprRsic6xxIyIJVQcbda9gZueEFWCoRyDyCgQ5YDVYkhfk" +
            "UkDdVVXJZDJZYHZbgA9fIKRFZ7PqCIvvCC5GkFiWZS//ojIprMbFBFKMYQGLCCeVAzCc9ulsZpQw8NjA6Teq2aD3He81nU7l4GOP" +
            "yR6lcb1SdfBcm//kk0/KnXfc0RM09CbzOBe4IQx+mmrmg8Cr6zppNSA2ljXhE3Vdy2w+73snqPJncwi0uDYhfkUIQTYnE2P1nlpb" +
            "69vX1M2i7x/tejhYyDiYFo/nmEwmkm5sbCwMb17o8FEwYU7zf5FKTCYT09uFWNHS0pI9DEw2/g2ES/gy/BwQKiCLXddJw3oCJJIA" +
            "C8IDo63bRfsSgAh+v1/cfvXbH/uYvPzyy/KqV73K3BLkczy5OUCzWHQALzCvmJ1so1/0uT0Ntx6Rihr7e1hSKJPC/090+CNUP/NB" +
            "mRwHLiPFc+7+5qGeo9GodwHcVDis0ydEJWL28FiHJPvBoOVAOTx49Qm1gLPEHAdczNT1pGfvqAvWK7mSZezRZwi/+OyRI1sOlzpX" +
            "4PdHf/RH8sgjj8gFF1xgr9l1nSmdparAgVtVVZWMRyNL4/CswACG09gHMq2nibSaWc3m8742Q8gqgmakd9YK3nWyublplxMiELCc" +
            "AKCqqjLGEbf6O+d6F8DcePwwZs0v0L81ZUFRYUSpHs/c5QiYq1AtRaoc0ebEouUGVeADULeGjm4kXeFIY2jKspQnnnjCAtfvx/R7" +
            "7+Vb3/ymfOYzn5GlpaWFAJZ79OaqwomDCPAHIBeeA1aOYXYekQfe/3w262cNwMrAtRC9C0pnLLuPeYIM4nEth4m8sJqoLSDFTtNU" +
            "fKbMEgYMoEHHhNGhZFxK6lQghC5MuyayCPxfS6XcXN83DmYEcgcr8mVAojjhHKiiyxeYwqlTp+T48ePfl+l3zsnG+rp87GMfW5ju" +
            "zZDt5sbGQvbCabFBrUrKHApvYXj2ZDaTyXQqa+vrMlWyKAS4gfxxXwWTYOCWOc7iOQYw77hEVnjSDAAXuCQpnp4QMpg4yfAo34KE" +
            "2rnxPSODxCiV6v5hcxZakFFUIljTBhiR8DSbR56th0APVDQrZyLI0/fI81xOnjwp33n0UQNdzvcA3HnnnfL000/3GD4hovlAiwfr" +
            "guwJ1T8eLhVCkFNrazKdzWSqmz7T2AnrgUOBdW/IXbCGAGsTGNBGegBI6VhVHPEU/h+zoBErAIMRkV4iZviCXPLFC0OLhoc4JOpj" +
            "mF0D0mWrwWPKt1U3cDQaLbSGIYrPBjpFwwMBfiArZC1MGHVONjc25LnnnrM5eudj+v/qwQflvvvuk9Fo1Fsc4s+xKgp49kA52Qps" +
            "bGxIF4Ice+klWd/YWGAOI8I3aJg2y1OnMNI0PmC44ayoyhL+CCThcrjUjsPBuoh8ofvWMP0QaDKAyUOfPDcqQuzJePqE1+PFW40P" +
            "PCZckzsoiqKfBqYHC/UAvIao9CpuEtTEOTD16usrJYgEEq6ANvF/Og9uADb/ueeekzvvvPN0P6T+21BhHJz/Tsur8/lcNicTmUyn" +
            "sr6xccbnbnWAI8vNsiqYzS4in86kHJjslopLaFzhDYTWoKMpZ6irIJ6w5hqaLm5WnadMMH2Z0wWkJSz11hFDCDk4YFgMWwb5MZJi" +
            "KKeZThcbDxFFpCwKCdr2xe3PADg6En8GGbKjVqiiKOTgwYMyn88XAJpXFGu+/Xb53ssvy8q2beYKC3ASu05m87nMq0o2Fceoqkrm" +
            "6MhRfyrUcYx+Ru70RQqGDWLsA5ZuNBotyLpOJhOj4cOcG8OXiCg4/KwAwoEgDiJiL64ttG0rKVebhmPMGMnjbhzU7FGN4ojehCAJ" +
            "IzeemmoAMPu10CFUQASDFn/wQDZCRiNgWAyOpHN1M2hjW1tbswXcCvD57Gc/K//x7/9elpeX7dYZzVqVyVmplLUUF0g0pNWLTWYC" +
            "B2Mo6HOEAAV+dqJVTmwqQJ3hNBYcUibhsKYAfpYHaGPgJVwG11X8MGpkjdrpdCqdduky8sbBDnezIGVDaRioGOKJSNp4eD+ceuTN" +
            "fmCGgfMPq2A2fFkPQqroWaIcwcM6bn5YGsbmHzhwQO6+++6+6LO2Zv/hxjckLs3yaynNSeZxrlyy5aldVu5Fz36W2URQjgeQovHt" +
            "5NQQhFs8Z6VyeVwp5HgCbgDj5nBQmefRtm0fBA5BINSjGalDR2tOY1LP0PKlEiX3qsMXpvp3pIzcemalzEHfISJfPERKY10xRxj+" +
            "lNvVoBvAxBVu4/71X/91WV9fN6QNJpJH5i2QYonPyBo+zEzCzyJ+gM/GoOdi0OQJKwBLwQM5gHRymx1+bjKZLATpCPh43hDLxkGM" +
            "G4cK76Wf/TS9izXkuO4Nq3A2sUJuRHQkFo3mT06j8FAR/AIiU3JwkimPHdYBUDQjjuC+IUbB54b8+vNn4QbgAPzO7/yOfPOb35Qd" +
            "O3bYZy6Ib8gbzaRZntwNHx9pRiA3lbDKOQ4SDjUsKi4VN3owFQ5uhXmBPLMB7mVjY2PBTTE8jQPPrGsbcpFl/fBoJltY0UAjXgAw" +
            "qEhh9CisxHAq5ayqrB0KhNJInTJFUci8qkS0mojTW5RlT4RQarTTk8vpSyC1Mi4R8yia6XQqXQjyjW9+cyH+gOn/0pe+JH/yJ38i" +
            "O3fuXOhMYheBQ8zaPCyODZ0fuDHwJltSObWAEdr+ejA4psEm8uGJRIbhhlmY75riEhyKpaWlBQ0mWGyTh9U9xDPhe845SVlihEWM" +
            "iqKQSDN2GFRgIQiuuoHMAMVN1AkYAq1U4YsFItM0lZnSqkZEu+KACwte6RArdNJsbGyY5BlHv0ePHj2jfvHCCy/Ivn37Fm4UB5gc" +
            "COP3ptPpwmQxI32qO0OAxXI0eD0uAfPcIVwEJm8yrgB/jzQcAV1NQhF8KTi/x63njMyUV6jkjxjLMwSMByzLsic5UAMCfA4CQ+aX" +
            "Q0+gqiopVYkKog9cJ0CPGgCgBD36GjRB+AmbbWPXNZBZX1+Xzc1N2ZhM5OSpU7I5mcjmZGI3DRYsxijHjh2TRx55xDaybVvZt2+f" +
            "HDt2zIATJl0y+JWQMCY2H5Ru5tWx6c5IGwCkjaWlpYVonvWT2L1wJgWzzpNOGBtAYIwLiJtucj6UrXDcAtCMay0hBEn27NnzWzZu" +
            "FN0syg7iFCYMBAyHI8sC9ctjpJq1QZ/lNAatnuH7bO5AAJ3rDQOMiqAGC49mFlbSZpz+61//ulxwwQVyzTXXyD333CP33HOPbNNO" +
            "XqBuKN0yS2Z4c+GPWdyJfSwXcTiWwbNh6jirr7PAFm4nB2hDNbOOMijOKjAQgxtJhwErVxFZ39k5J+4n3/GOyCPkOdcuy7JXudTT" +
            "PwxyGK+fzWZGa7bOXjKtpjyivjSjGGI6m52ek0vMV4ZjgSlABJHTMShwAFCCiZ3qMKUbb7xRvva1ry3IxTK5Y6ifawHloN2ab3Nd" +
            "1zIej3uBBwLNMEsAWn0gwmBDmGCD94RL4VQXwS27WhwU/MlikzyAC/Ec0+zYFfIAsOSySy/9rWEdgGvpVVXZwAjOqc3XkIAD/nVB" +
            "8lw/jGnaqmWpm0YmasKRYeDmcSqECB+bxiaM01e+rbhZcE/f/e53F2Yf8eAIE1+kwRlMneKxbaawTdNR+bByRoO/w1pxDwRH+1w9" +
            "BVfC0yxArolwzLVQmSVZPtZ55MIcZzhgKms3d2bADaRSwXDFopjah8rItipwlCaJiJoVXmD4JfD5ESyh8zfRHNhTTyCqcDCVSP04" +
            "AMTrD0vHnBUsyKxQDYJFn4fVNyweYggOtnjTkPLhdRAgslY/Zy1c9m5pzhLXKIANgEfAuANjA0OgDsEm1x7wOfFvm5ubC8RSZDIc" +
            "73nc/EAKYJBl5xo0LAG6hlIq/gSqTkURWV9fl6Zt5eTJkzKdzWRzc1Mm06lMNGCD4odj00b6wJh/wzNxTHNHby9OPxYBboGJE8ho" +
            "mDrNhEkWxhKaVso3kCd7cWUU5hm3idnMfGA4M8FhZI4AmkrwPjDdPEAK1mKmg7xYSoaDR9YGZL1DxDU8Xt7Y4DwmNkkSaUlZmnlv" +
            "mOaBE8UvtLm5KZPNTVP0yAlLgBIWRB5FsQGAPSZZonkxfCyyAES9OLkMpjBLmaPqpaUlw/PZjOI9sNg43AyOMEkTLo8pbrBwjjqa" +
            "8PlAu4LbYpEGZuyye+LmUMRda2trln3g9YG7YK0QC7CUL9cD8BzIdDi1xWevqkqSSy+99De89wk+KI8rAx0810gTQsMoKsxms57l" +
            "o5rBEDZEi1hVVTbqDe1M2FiYSxxAKHbzlKzhCHrceD8YLY9bn1JrNTew4GFhXbCRXGRhqjTn0lwS5jybYxH+/7ZtZTKZWFTOnxku" +
            "g3kGTN7EBqNgxPUHFtfipl3rBdQ/+fA66hvISLkFazEajWIaQpilaZpFvbkG9qhCV9O2Uil13IY3DGrq3nspx+N+GJNucl3XfZoH" +
            "ObIkkUx/lsu8XLRgk4ab5GimL+jPfPMZ6eIgkDXzYcEY8EpII5mj5GHRaJghcCbEZFpkBZwOcum3ISl667Ek1TNO/YDG4uYzDDyc" +
            "EQRCCNaVU0eeK8hBKFmz4Mfj8ck8z2VeVXF9Y0Oqupa5Kn2vr69LpeSHVk06T7Qy/p6yZTCPdz6bSaDR61zgOVvLM99wPMDy8rLd" +
            "YJ5tCEyerQgDWayZx82TzGng6SfwyUyrwmdjthOiZqaP8/sjcOWInTujGS9gsAasbGYIcbsYDhGGbdoALf2sOHQNdSLByvJEUaTq" +
            "qAtoVlMnF1100Xtn8/nFKljg1tfXT0+lUkYO5M6KsrQauQETGi94nSAORlDQNm82T7hFMFWmiKknl/05E1DwgMjLGVji/JZL1fDH" +
            "bDJ56tnQFy7M1yV2zvBw4P1QqRsqe2GjsW5crMLG860ewsOojbB14WlsCzJwdMux0ThQHC+gSoiYJU3TmCSJE5EjSZqmb/HeX5+m" +
            "aaiqyjvnrLcPY+FF/QYHF6wtWKmZ5UCroc3hjhjuaefUhv0uq4EiEORS8JBKhdvCShgAQNgtcMBnQgk6T4CroYzJs/4xgj1sIsOr" +
            "DF2fraV+iD5iLRi84RvOzTqoDVhaTpByMxjXN6wqYs2YYJqmaRARP5vN7k927dq1xzl3oxOJWZZ5TomgSc+cAZRrTeIVsrLU/8fy" +
            "5cwtHKZIQ8IGf3jmGnB+zDc/0Jh0RsDwO3AT4/F4oWmCbxX+Q/A7jE1YI4HhWNwwBJtMmFlAPpVrAHfGABkXZ4bBLgePTD1nuJkr" +
            "gHBzjJ6yVWLWkHMuxhi99/7fJBdddFGTZdn7vHNpQ3g0PmhDp5N1+3jxjKvetiaAwCaUMXq8PgIinmrFKBr7W74R8MdIs4ZWhptJ" +
            "+cZw+zQvMLdUsWAV1zE4dhjOUOR6AKedHNOwrF5LEjHMV0Cuzu6S2dSMwPJFQVbBqS0TXLjdjy6Z77qurev6N/0b3vCGb6Rp+phz" +
            "LiZJEnhT4yAShkKVp0EF4LFj8/nk8WYyjZk7h1hKhiFN7j/ATUOgxfP0GM9nYURmytRDvX26rZx64qYh60CgyL+LA4uDyA0gXJXD" +
            "c3BgyNH5sAOKp6Byyset8kglGbo2hjXNK2I6OtaDALCQJEmMMT52/fXXfys5ePBgfNWrXnVBkefvCDGGuq49jxyHYDHmCCaqggUV" +
            "8Kqu+5hBI+oFSBTSJ7qhKekGsEvBBxRSFuHpGcP+QcQROc02HJZvh4qmuMEsb8dsG5ZOG35GmOmF9TiLxi/3/5t6B3USwRdjPRjh" +
            "ZDFp+HwErkyExfpC2nfYy8kXhaeeEXQcsizzXdd94u/+7u/2Y3r4vc65mRPxeZ7HJa1FL8y3UaTM0il0sJBgERi1Zpa1Xs0FF0cc" +
            "ADb1+B6PsQU6xm6CCx1srbj4wWlWWZYyHo8NveONGWISHc3vweFBWxWrb7MlgrnlGj/364FUy2kbR+gmrjkgmHKqBwsYYzQsBKof" +
            "LOTFwBd4gGzRRqNRHI1G3onM5mtrfyoikqyurib79+8//l9cdNEVMYR/kqZpaJrGc82Yq0lo2eZZ92h75huF36l0Vt1QGIIXajgp" +
            "a5jXY+P4s+BU8/g05tdxDs3RMPtnjugZDeRshalaHIhySsYETMbm8XvIoBhcAr7AEDYfTtQF+D1RG+Fq48JEdv2MeE5YU4opQozR" +
            "+yT5twcef/ye1dXVxF977bUYYP27IcZKnHNt20YTggSlm4SjWxWFwGFgX4+DwFgBFgcPA8x9OLoek8DY1PHP4YZwX4D14lNtHaaR" +
            "zSpjBux+WDYemwarBSFoHC5UFbtB4QrvwVA0InTmEjJNDK6KK3ktUdGLojClb07xgIUgQLU4jFwaMhh2C977KCKubdtqY2Pj/xAR" +
            "d+2118Zk//79UUSSo0ePvrRz586VxLmfaNq2W1pa8vjwNq1DFTEgkDSsXc+0dZrbmxnx4yYUPAhMKle34mDsOXMRmFzBQyM52uVZ" +
            "ewCduBI3BFGGtDDrfKL3ZVh32ADCaCa7saEEr5AgNB9IVj/DvzPeMbTGAHzYx/Pacjc3BYddlmVJCOF3Dx06dK+IJPv37w8A9oOI" +
            "+KuWlm5r2vZgjDGdzWadASc6uAjiiChL8mby0Admv+C/qqrOiriBKwAzznk1/n14cxeGU1FgCHAGPQtcA+f4ZCjAOJRiY+wChx/P" +
            "y1E5onzQ1PFZOVVbmLNAlC6e7MEcRC6CcQBq5Xa9bEjzwA0Ykll5jZMk6ZaXl9O2bQ8eOHDgfxURr3suOABRROT+b3xjGkXeWxRF" +
            "FUVcCCFCwxal3iWdBciVOuectTZxXs23l/v+h/+GmwjIktMugDg8S5iHJuH1uJ+fCSMMvuBgcacumjMQ2MFnTiYTo5AxsMONMdDe" +
            "5SyBNxe0NG6952yIzTe7I1hCvLahr4QfgKKWqvIo/p/1gtW6xKZpXNM0VZqm7xWRKe85t8+G1dXV5MCBA19Ps+z9aZL4ruu6EELE" +
            "wOVhfxwURFBe5AIEKlrDDlkEXxBAYhIH5+3cVsY8eZ5G2gxUQ9il4NYgDgFfkeXwudEDAS38Pg4XDiYrjPM0Mc6WkKez7+XRuJiV" +
            "gNuLeINrBAjquOcCB5FdFawRk01hDcgaxzRNuyRJ/Hw+f//DDz/89dXV1QS3X1SpzL4OHjwY9+7dmz700EMPX3zxxZKl6TuiSJAY" +
            "Xd00bhjhMyDDxAQupphAEhVs2F1wb/yCXLwGVczo4YPGRRssArsRbqxggAafg7tpmb7FqhxcWsWBY4bwkKWLkjCTZpEqsrA1cvzh" +
            "AG2sLweyONzc08fcP4aSkY2odY1t24Ysy9K6rn/rscceu2vv3r3pAw880PGen6GjduTIkbi6upr8zd/8zd/u2rWr8N6/tes613Zd" +
            "cHpsbbIV1Zh55BtX3IbIFDh0Q27cUKiRH5T76RnswWJy8AYaFPP98JkZjx92FjHJkrX44Jc5SOSAjAdsMwyM1+X4iLMaBnQYYuZn" +
            "tmYaZUWxa+VGEc4K9GdC13U+yzLfNM3/fuDAgX2rq6vJAw88cIZkylmF9A4ePCgi4o8ePfrX27dvXxsvLf3UaDTyVVW13nuXpqnj" +
            "IQ9YdKaEY5MQGDHKNvS/+H3cYDRUMk0bD8zvJSQnyzm4tZtTigZXxNU0Fqti7QOAP1yoYt8LUUwutcIycNs4Wy9cFhZp4oPA9Qq4" +
            "T3w+k8uny8BWil2MiLTOuSTGGIo0/fAj3/72x0TEHzx4MJ5NNOWVJDSiiMS9e/emhw4duqOu65+cTqdP5Hmeeu9d13VtlmWRHxg3" +
            "k/0up3X4E1g297jjdYCvI7LmuALIIE/KNjVyGkWPmcIgW5QkXjUUScB7BtJJqqpK1tbWLKhDEIwUEjV7HGZGJ7mbGl9ra2sLbgjP" +
            "gMN7tsPMhSa4uZSgduYo6kGL3vtWqWSpiDzeNM07vvHww3epz4+vpJizpZTmkSNHgogkx44de2o6nd67ffv2UQjhR8qyTEMILsbY" +
            "UiDkGCod8uK572CIinF38lAflwMuRuGwaYz3w4cyFMtcPaaPL0jfUHkYmAGbfRarYg4ivx8QT/wco5lwCVy4AQ7CdX8ceOYCsgUb" +
            "6CdH731wzoUQQtJ1ne+Xov29tm3fe/jw4cdkdTU5+PnPd1vqJp+nmloiIp2IyDXXXPMjWZZ9IMb4nvF4PCZN+6Aph/feO5gx9sFc" +
            "uuV5QdzPDt++ubl5hqIV+0dGxhrS8GcxZRZh4DiBYw9Ow8qytHSWA9ehhsFsNltA/vAszGJmOj3P8OEAlOcCsfVgKtwAq4hd1wXv" +
            "vUovebzHdDwe/+lkMvnk448//o/DPfvPcQDwsx4vumfPnit27ty5Op1Obw4h/JOyLLchxYH55FvNwygYL2dps0hyb3zLcRA2NjYW" +
            "kEMOtligmgtI3F3LUi78b9AjquvaRCLRmMKwNnfmDnkFgGDX1tYWCjBDVVDOhvhQMnmDW8BZUQVYwWg0ktlsti4iX8+y7K+m0+mf" +
            "Hz58+Cna+HAeImnf9wHguMHx6bryyisv3b59+8/Mp9M3RJEfF+euTJJk5JxLcSs4+p5Op7K8vGwbg40c4vXYTNbh4codg0HcSczd" +
            "MVyfZyiVgz5G9xDUwl9j3gGTNTgd5hmECOQQ+aM3kQmlyFDQx8jzlhh/ALilPI3Oez/vuu6ptm3/XxE54Jz7woEDB57DWq2uriaf" +
            "//znI+f45/P1/wHnirqy5YjcpAAAAABJRU5ErkJggg==";
    }
}
