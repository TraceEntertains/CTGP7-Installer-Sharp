# CTGP7-Installer-Sharp
This is a C# port of [CyberYoshi64's CTGP-7 Installer](https://github.com/CyberYoshi64/CTGP7-UpdateTool) made using the Avalonia UI framework and ChatGPT for the tedious parts of porting Python to C# (like porting code between a lot of other languages, most of it is the tedious part)

Generally most stuff from the original applies to this version, the UI placement is very similar, but just in dark mode on OSes other than Linux. Definitely some bugs still, and some odd compilation flags (that make sense to me but can get a tad confusing at times). But hopefully soon it should soon be in a "Ready for general public" state.

Footnote:
This likely **_will not_** replace the original installer, and instead will attempt to mirror code changes made in the original/development builds of the original (as shown in error screens with the "Python installer version equivalent" text). It also currently has some non-updated info text that I need to fix (mainly in error screens), if I ever get around to it, I will do a custom MessageBox in Avalonia because the library I'm using honestly just isn't cutting it. There aren't any releases yet either and I still need to implement the dev branch code using the flags I added recently.
