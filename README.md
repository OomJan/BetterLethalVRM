# Better Lethal VRM

A VRM mod for Lethal Company based on LethalVRM by Ooseykins. This variant of the mod adds support for springbones, fallback avatars and the possibility to name your VRM files as your steam name (Steam ID is still supported).

Replace player models with custom VRMs. This client mod requires some setup to see other players as VRM avatars. This is a tool specifically for Vtuber collabs, not a general-purpose model replacement for Lethal Company. Do not expect this mod to "just work" right away with your model, some edits to toggles and materials might need to be made. There are no models included with this mod; VRoid Studio (on Steam) models are free and fully compatible.

This mod requires you to send your VRM to the other people you're playing with! Make sure you trust the people you are sending your VRM models to!

## VRMs

Browse to your Lethal Company installation (Find the VRM folder by going to your Steam library and right clicking Lethal Company -> Manage -> Browse Local Files.)

Create a folder in your Lethal Company folder called "VRMs" (if it does not exist already) and place your VRM file into that folder. Your VRM can be named using your steamID64 (dec) or your Steam username.

* Example of using the steamID64: 76561197974711290.vrm
* Example of using the Steam username: OomJan.vrm

Since it is possible that two users have the same steam name, the Steam ID is preferred before the username.

## Fallback

Create a VRM named "fallback.vrm" and place it into your VRMs folder. When ever a player joins who has no personal VRM file in your "VRMs" folder, the fallback will be used.

## FAQ

**Q:** Do my friends need the mod to see my avatar?
**A:** Yes.

**Q:** My friends have the mod, why can't they see my avatar?
**A:** The avatars have to be configured on all players clients, with the VRM files named after each appropriate ID. It seems more of a hassle than just automatically sending your avatar when you join a server, but there are 2 reasons this doesn't happen.

Most Vtubers try very hard to keep their model files safe, and sending your VRM to another person can be a little scary. Accidentally joining the wrong player would send them a copy of your model, and the inconvenience can be worth the protection. It's important that you trust the players you're playing or collabing with to not share your model around.

The other reason is that some VRM files are very big, and downloading them each time you join a server is a lot of data. Things are just a little simpler this way, if a little inconvenient.

**Q:** Why are my arms still the default character?
**A:** This mod only changes the 3rd person model. You will be able to see your own custom model while spectating other players.

**Q:** My avatar is a plain white texture, how do I fix this?
**A:** In most cases this seems to be caused by using a non-MToon shader material when exporting the VRM. Make sure all of the materials are MToon. The console log will show an error about this if a material is not MToon. If changing to MToon doesn't fix things, try using the MToon10 shader if available in your version of UniVRM.

**Q:** My model is very short or very tall, will that break things?
**A:** Hopefully not. The mod will try and scale your character and position the avatar's head at or near the regular player head height. If your model is scaling very strangely compared to the normal Lethal Company character, feel free to message me about this error! Moving the avatar's head bone (start, not end) in the armature before exporting the VRM can be used to change the avatar scale. If the avatar is appearing too big, move it up; too small move it down.

**Q:** Something about my model looks weird, bumpy, or wrong, why?
**A:** Unfortunately this mod requires many hacks and workarounds to make VRM work in Lethal Company. Most material properties from VRM will not carry over very well. This includes outline, non-cutout transparency, emissive, rim lighting, matcap, and maybe more. Some models are purpose built for Vtubing and may use modelling and texturing techniques that just don't look right in game.

**Q:** Does this mod work with MToon shaders?
**A:** No, but use these shaders anyways when exporting your model. VRM does not officially support the rendering pipeline (HDRP) used by Lethal Company. Workarounds were made to import the VRM and MToon materials and make them look like the style of the game, they won't keep their toony appearance.

**Q:** Does this mod work with spring bones?
**A:** Yes.

**Q:** Does this mod work with VSF Avatar, or some other model format?
**A:** No. VSF avatar is a different format with advanced features that would just be ignored on import. LethalVRM is created for Vtubers and targets the most popular avatar format to reach the widest audience possible.

**Q:** Why are all my avatar toggles on?
**A:** There isn't a simple way to configure your character toggles as part of LethalVRM. If your character has toggles such as alternate hair or outfits that you'd like to remain off you will have to create an alternative version of your VRM with these parts removed. There are many VRM guides available online.

**Q:** Why is my thumb all messed up? Why are my hands not holding things?
**A:** The animation skeletons used for VRM and Lethal Company are both very non-standard. I think the broken thumb part is from VRM, and the hands being far away is likely due to the model's shoulders being narrow. There isn't really a fix for either of these things, just try to embrace the crustiness of Lethal Company.

**Q:** Does this mod work with MoreCompany?
**A:** Yes.

**Q:** Does this mod work with some other mod?
**A:** ¯\\\_(ツ)_/¯

**Q:** Can this mod handle my great, great assets?
**A:** YES

## Technical Stuff

I don't like speculation on how technical things are handled in mods, so I thought it might be helpful to list out the way some things are achieved. There are a lot of (probably unnecessary) hacks used in this mod, if some programmer nerds know some (not wild speculation) ways to get around my hacks, I would appreciate the comments.

- **Loading the template materials**: I wait until the ship scene is loaded, then find the catwalk object outside the ship ("CatwalkShip"). This object has a renderer with a cutout material that I can use as a template for later materials to be put on the character models. There might be another way to load the correct material shader with the right flags set, but my hack works OK. The initial shader loaded by the VRM api is extremely basic and acts as a dummy to populate the correct fields later on.

- **Posing the template player skeleton**: When a scene is loaded, it searches for a player and creates a template. The player placed in the scene is in a really awkward pose, so I manually set the bone angles for the upper arms, upper legs, and hands, to match the T-Pose expected by VRM. This template player is created once during the lifetime of the application and remains hidden and disabled forever.

- **Spawning a VRM**: There will only ever be one VRM spawned for each player, and it will be positioned and posed at their player, ragdoll, or mimic, depending on the state. Lethal Company has separate renderers for each of these 3 states, but performance can get really bad if new VRMs were spawned in each of these cases. The VRM game objects are spawned with the UniVRM 1.0 api and some VRM components (like animators) are disabled on spawn.

- **Adding reference bones**: When a VRM is attached to a player, ragdoll, or mimic, extra transforms (bones) are added for each humanoid bone of the model, using the template player bone rotations as a base. When positioning the VRM models, it uses those reference bones to set the angles correctly. This is probably possible without these extra transforms, but I'm really not that great at quaternion/rotations math.

- **Handling visibility**: I handle visibility of the model using some simple checks against the player's death state, and some variables as part of their dead body ragdoll. This means there's weirdness of seeing only your characters first-person arms in the cameras since I'm only disabling/enabling the renderers. There is also weirdness involving players killed by the tentacles at HQ that might need fixing. There are probably some fixes to these issues that can be done by using the correct layers for the renderers and camera culling masks, but it needs some investigation.


## Special Thanks

Special thanks to Ooseykins for creating the initial mod and giving me some insight to their work.

## Contact

Discord: oomjan
Twitter: https://twitter.com/OomJan34

## Source

https://github.com/OomJan/BetterLethalVRM

## Compilation

### Required software
* Unity 2022.3.9f1 via Unity Hub
* Microsoft Visual Studio 2022

Check out the GIT repository above and open the Unity project containted in the folder "Unity" to compile all the needed the dependencies. Opening the project is enough and waiting till Unity has build everything. Now you can open the solution in the root folder.

## License: MIT License

Copyright 2024 OomJan, Ooseykins

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the “Software”), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.