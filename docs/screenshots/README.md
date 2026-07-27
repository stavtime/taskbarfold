# Screenshots

Files the site looks for. Both language pages (`docs/index.html` and
`docs/en/index.html`) read from this one folder, so a file dropped here shows up
on both.

A missing file is not a broken image: the slot renders as a labelled
placeholder with the expected name and size, so the page stays presentable
until the shot is dropped in.

| File | Used in | Suggested size | What to capture |
|---|---|---|---|
| `flyout.png` | Hero | 1280 x 720 | The flyout open over a real desktop, taskbar visible |
| `taskbar-before.png` | "Why" section | 1200 x 200 | A crowded taskbar, many separate pinned icons |
| `taskbar-after.png` | "Why" section | 1200 x 200 | The same taskbar with four TaskbarFold groups |
| `search.png` | Features, large cell | 900 x 640 | Type-to-search filtering the flyout, digits visible |
| `settings.png` | "How it works" | 1280 x 860 | The settings window with a group being edited |

Keep the two taskbar shots at the same width and crop, so the before and after
line up when they sit next to each other.

PNG, no window shadow baked in (the site adds its own frame), and take them at
100% scaling on a dark desktop if possible.
