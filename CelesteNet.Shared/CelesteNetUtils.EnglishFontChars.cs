﻿using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.Helpers;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet {
    public static partial class CelesteNetUtils {

        // Yes, this is better than trying to find the original font file and parsing it.

        /* 2022-12-18 - RedFlames
         * I went over the "stranger" looking ones here (i.e. I skipped the
         * big chunk in the middle that "look like letters" and all that...)
         * and commented out some like 0xAD (soft-hyphen) that don't render
         * properly.
         */

        public static readonly char[] EnglishFontChars = new int[] {
            0x0020, //  
            0x0021, // !
            0x0022, // "
            0x0023, // #
            0x0024, // $
            0x0025, // %
            0x0026, // &
            0x0027, // '
            0x0028, // (
            0x0029, // )
            0x002A, // *
            0x002B, // +
            0x002C, // ,
            0x002D, // -
            0x002E, // .
            0x002F, // /
            0x0030, // 0
            0x0031, // 1
            0x0032, // 2
            0x0033, // 3
            0x0034, // 4
            0x0035, // 5
            0x0036, // 6
            0x0037, // 7
            0x0038, // 8
            0x0039, // 9
            0x003A, // :
            0x003B, // ;
            0x003C, // <
            0x003D, // =
            0x003E, // >
            0x003F, // ?
            0x0040, // @
            0x0041, // A
            0x0042, // B
            0x0043, // C
            0x0044, // D
            0x0045, // E
            0x0046, // F
            0x0047, // G
            0x0048, // H
            0x0049, // I
            0x004A, // J
            0x004B, // K
            0x004C, // L
            0x004D, // M
            0x004E, // N
            0x004F, // O
            0x0050, // P
            0x0051, // Q
            0x0052, // R
            0x0053, // S
            0x0054, // T
            0x0055, // U
            0x0056, // V
            0x0057, // W
            0x0058, // X
            0x0059, // Y
            0x005A, // Z
            0x005B, // [
            0x005C, // \
            0x005D, // ]
            0x005E, // ^
            0x005F, // _
            0x0060, // `
            0x0061, // a
            0x0062, // b
            0x0063, // c
            0x0064, // d
            0x0065, // e
            0x0066, // f
            0x0067, // g
            0x0068, // h
            0x0069, // i
            0x006A, // j
            0x006B, // k
            0x006C, // l
            0x006D, // m
            0x006E, // n
            0x006F, // o
            0x0070, // p
            0x0071, // q
            0x0072, // r
            0x0073, // s
            0x0074, // t
            0x0075, // u
            0x0076, // v
            0x0077, // w
            0x0078, // x
            0x0079, // y
            0x007A, // z
            0x007B, // {
            0x007C, // |
            0x007D, // }
            0x007E, // ~
          //0x00A0, //   - see comment at top
            0x00A1, // ¡
            0x00A2, // ¢
            0x00A3, // £
            0x00A5, // ¥
            0x00A6, // ¦
            0x00A7, // §
            0x00A8, // ¨
            0x00A9, // ©
            0x00AA, // ª
            0x00AB, // «
            0x00AC, // ¬
          //0x00AD, // ­  - see comment at top
            0x00AE, // ®
            0x00AF, // ¯
            0x00B0, // °
            0x00B1, // ±
            0x00B2, // ²
            0x00B3, // ³
            0x00B4, // ´
            0x00B6, // ¶
            0x00B7, // ·
            0x00B8, // ¸
            0x00B9, // ¹
            0x00BA, // º
            0x00BB, // »
            0x00BC, // ¼
            0x00BD, // ½
            0x00BE, // ¾
            0x00BF, // ¿
            0x00C0, // À
            0x00C1, // Á
            0x00C2, // Â
            0x00C3, // Ã
            0x00C4, // Ä
            0x00C5, // Å
            0x00C6, // Æ
            0x00C7, // Ç
            0x00C8, // È
            0x00C9, // É
            0x00CA, // Ê
            0x00CB, // Ë
            0x00CC, // Ì
            0x00CD, // Í
            0x00CE, // Î
            0x00CF, // Ï
            0x00D0, // Ð
            0x00D1, // Ñ
            0x00D2, // Ò
            0x00D3, // Ó
            0x00D4, // Ô
            0x00D5, // Õ
            0x00D6, // Ö
            0x00D7, // ×
            0x00D8, // Ø
            0x00D9, // Ù
            0x00DA, // Ú
            0x00DB, // Û
            0x00DC, // Ü
            0x00DD, // Ý
            0x00DE, // Þ
            0x00DF, // ß
            0x00E0, // à
            0x00E1, // á
            0x00E2, // â
            0x00E3, // ã
            0x00E4, // ä
            0x00E5, // å
            0x00E6, // æ
            0x00E7, // ç
            0x00E8, // è
            0x00E9, // é
            0x00EA, // ê
            0x00EB, // ë
            0x00EC, // ì
            0x00ED, // í
            0x00EE, // î
            0x00EF, // ï
            0x00F0, // ð
            0x00F1, // ñ
            0x00F2, // ò
            0x00F3, // ó
            0x00F4, // ô
            0x00F5, // õ
            0x00F6, // ö
            0x00F7, // ÷
            0x00F8, // ø
            0x00F9, // ù
            0x00FA, // ú
            0x00FB, // û
            0x00FC, // ü
            0x00FD, // ý
            0x00FE, // þ
            0x00FF, // ÿ
            0x0100, // Ā
            0x0101, // ā
            0x0102, // Ă
            0x0103, // ă
            0x0104, // Ą
            0x0105, // ą
            0x0106, // Ć
            0x0107, // ć
            0x010C, // Č
            0x010D, // č
            0x010E, // Ď
            0x010F, // ď
            0x0110, // Đ
            0x0111, // đ
            0x0112, // Ē
            0x0113, // ē
            0x0116, // Ė
            0x0117, // ė
            0x0118, // Ę
            0x0119, // ę
            0x011A, // Ě
            0x011B, // ě
            0x011E, // Ğ
            0x011F, // ğ
            0x0122, // Ģ
            0x0123, // ģ
            0x012A, // Ī
            0x012B, // ī
            0x012E, // Į
            0x012F, // į
            0x0130, // İ
            0x0131, // ı
            0x0136, // Ķ
            0x0137, // ķ
            0x0139, // Ĺ
            0x013A, // ĺ
            0x013B, // Ļ
            0x013C, // ļ
            0x013D, // Ľ
            0x013E, // ľ
            0x0141, // Ł
            0x0142, // ł
            0x0143, // Ń
            0x0144, // ń
            0x0145, // Ņ
            0x0146, // ņ
            0x0147, // Ň
            0x0148, // ň
            0x014C, // Ō
            0x014D, // ō
            0x0150, // Ő
            0x0151, // ő
            0x0152, // Œ
            0x0153, // œ
            0x0154, // Ŕ
            0x0155, // ŕ
            0x0156, // Ŗ
            0x0157, // ŗ
            0x0158, // Ř
            0x0159, // ř
            0x015A, // Ś
            0x015B, // ś
            0x015E, // Ş
            0x015F, // ş
            0x0160, // Š
            0x0161, // š
            0x0162, // Ţ
            0x0163, // ţ
            0x0164, // Ť
            0x0165, // ť
            0x016A, // Ū
            0x016B, // ū
            0x016E, // Ů
            0x016F, // ů
            0x0170, // Ű
            0x0171, // ű
            0x0172, // Ų
            0x0173, // ų
            0x0178, // Ÿ
            0x0179, // Ź
            0x017A, // ź
            0x017B, // Ż
            0x017C, // ż
            0x017D, // Ž
            0x017E, // ž
            0x0218, // Ș
            0x0219, // ș
            0x021A, // Ț
            0x021B, // ț
            0x02C6, // ˆ
            0x02C7, // ˇ
            0x02D8, // ˘
            0x02D9, // ˙
            0x02DA, // ˚
            0x02DB, // ˛
            0x02DC, // ˜
            0x02DD, // ˝
            0x2013, // –
            0x2014, // —
            0x2018, // ‘
            0x2019, // ’
            0x201A, // ‚
            0x201C, // “
            0x201D, // ”
            0x201E, // „
            0x2020, // †
            0x2021, // ‡
            0x2022, // •
            0x2026, // …
            0x2030, // ‰
            0x2039, // ‹
            0x203A, // ›
            0x2044, // ⁄
            0x20AC, // €
            0x2122, // ™
            0x2260, // ≠
            0x2264, // ≤
            0x2265, // ≥
        }.Select(i => (char) i).ToArray();

        public static readonly HashSet<char> EnglishFontCharsSet = new(EnglishFontChars);

    }
}
