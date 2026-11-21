# The item and skill message files' serials start at 5143, 5916, 6749 and 7835

- **Game build:** `548ddc955c176867f062c44f94c03dd9ac33caefb278a5ac388e792aedf09504` (Steam build 18330018)
- **Method:** Opening the shipped executable in a disassembler as-is, accepting that protected parts may be unreadable
- **Sources:** none, see Steps; builds on
  [`message-serials-from-file-positions.md`](message-serials-from-file-positions.md)

The exe's message-file table gives each file a range test. For the four item and skill files they are: names
`Item_Message_1.msg` 5143–5915 (773), descriptions `Item_Message_2.msg` 5916–6748 (833), `Skill_Message_1.msg`
6749–7834 (1086) and `Skill_Message_2.msg` 7835–8886 (1052). For #247, so battle's skill and item lists can say a row's
name from the serial they fetch it by. Static only.

## Evidence

- **The table's entries.** Each entry is 0x40 bytes in `.debug`: `+0x8` a pointer to the file's path from `message/`
  on, `+0x10` the path's length, `+0x18` a pointer shared by every entry (a callable's vtable, by its place), `+0x20`
  the range test. `System_Message.msg`'s entry is at `0x27b10d8`, next to its test `0x9f4c90` that the earlier note
  read. The four files' entries are at `0x27b1828`, `0x27b18f8`, `0x27b19c8` and `0x27b1a98`.
- **The range tests**, each `lea eax,[rcx-first]; cmp eax,count-1; setbe al; ret`:

  | Test | Bytes after `8D 81` | First | `cmp` | Count | File |
  |---|---|---|---|---|---|
  | `0x9f4a90` | `E9 EB FF FF`, `3D 04 03 00 00` | 5143 | 0x304 | 773 | `message/system/Item_Message_1.msg` |
  | `0x9f4aa0` | `E4 E8 FF FF`, `3D 40 03 00 00` | 5916 | 0x340 | 833 | `message/system/Item_Message_2.msg` |
  | `0x9f4ab0` | `A3 E5 FF FF`, `3D 3D 04 00 00` | 6749 | 0x43D | 1086 | `message/system/Skill_Message_1.msg` |
  | `0x9f4ac0` | `65 E1 FF FF`, `3D 1B 04 00 00` | 7835 | 0x41B | 1052 | `message/system/Skill_Message_2.msg` |

- **Against the 2026-11-20 run (#247, #584).** Battle's item list fetched 5143 with flags 9 and 5916 with flags 7, and the
  skill list 6994 (flags 9) and 8082 (flags 7). So the names are in `Item_Message_1` and `Skill_Message_1`, and the
  descriptions in the `_2` files. An item's name and description sat at the same position (0 and 0), a skill's didn't
  (245 and 247), so the files aren't read as parallel.
- **What's open.** That the game compiles these files with these counts, so `MessageSerials` keeps them, is
  `[NEEDS VERIFICATION]`: the earlier note matched `Item_Message_1`'s compiled count only. Which callable the shared
  pointer is wasn't read.

## Steps

`tools/Metaphor.SignatureCheck` (this repository at the commit that adds this note), on the build above, 2026-11-21:

1. `dotnet run --project tools/Metaphor.SignatureCheck -c Release -- --bytes <exe> 0x27b10c0 0x80`: `System_Message`'s
   entry, its path pointer, length 0x21 and test `0x9f4c90`. Reading the path pointer's target (`--bytes <exe>
   0x2122b50 0x20`) gives the path.
2. The entries in `0x27a0000`–`0x27d0000` were listed by a throwaway script (kept outside the repository) that reads the
   exe on disk and prints each 8-byte-aligned slot holding a pointer to a string of the next slot's length, followed by
   a pointer into `0x9e0000`–`0xa10000`. Check one with `--bytes <exe> 0x27b19c8 0x28`.
3. `--bytes <exe> 0x9f4a90 16`, and the same for `0x9f4aa0`, `0x9f4ab0` and `0x9f4ac0`: the range tests above.

Nothing in `.text1`, `.xcode` or `.xtext` was read.
