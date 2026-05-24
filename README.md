# WasemSalmehDrifty

Starting empty

The first real step was preparing the empty Unity scene and deciding what kind of game it should become. I wanted a racing game with a simple track, a controllable car, and a camera that makes the player feel like they are actually driving. I started with the basic scene setup, lighting, camera, and object placement. Some of the first objects were only placeholders, but they helped me understand the scale of the game and where the player should start.

I also had to think about the structure early, because later the project was going to include maps, vehicles, sounds, scripts, and UI. I learned that if everything is left loose in the hierarchy or folders, the game becomes confusing very quickly. Later on I organized the scene into groups like map, player, cameras, lighting, gameplay systems, and runtime objects so it would be easier to understand and edit.

Making the map

The first map was the desert track. I did not want it to be a plain circular road, so I shaped it more like an irregular racing track with curves and a clear finish line. I added asphalt, sand, curbs, barriers, decorations, and small environment details to make it feel more like a racing area. There were problems with white road lines and extra objects showing up in places where they did not look right, so I removed the unwanted white markings but kept the finish line because the race still needed a clear start and end point.

Later the desert map accidentally got mixed with the forest map while switching between maps. To fix that, I separated the map logic so the desert objects and forest objects are activated correctly depending on which map is selected. I also organized the map objects in the hierarchy so the desert track is under its own map group, and runtime map objects are kept in a separate runtime section instead of floating in the scene.

Adding the car (the player) and the mechanics

After the track was ready, I added the car as the player and worked on the driving mechanics. The car needed to move, turn, brake, restart, and feel different on asphalt compared to sand. One of the main problems was that the car did not move correctly at first, so I connected the existing car control scripts and adjusted the setup until the player could actually drive.

I added drifting too, but the first version had problems. When drifting was on Shift, the car could start spinning too strongly and became hard to control, so I changed the drift key to Spacebar and adjusted the drift behavior to be smoother. I also improved the mechanics at high speed so the car would not lose control too easily. Driving off the asphalt now makes the car slower and less smooth, while driving on the road feels normal again.

To make drifting feel better, I added smoke, tire trails, and drifting sounds. Some effects did not look right at first, especially the smoke texture and drift sound, so I fixed the effect setup and replaced the sound with something that fits better with tires sliding on asphalt. I also added the R key to restart the game and return the car to the starting position.

Add vehicles and a main menu

Once the main gameplay worked, I added more vehicles and created the main menu. The menu includes Play, Choose Vehicle, and Choose Map. I wanted the menu to feel like a racing game, so I added related colors, racing stripes, buttons, vehicle previews, and best time text. At one point the buttons could not be clicked, so I fixed the UI interaction setup to make the menu usable.

The vehicle selection also needed work. Some vehicles had pink or broken textures, so I fixed their materials and made sure the available vehicles looked usable. I removed the truck and trailer from the selection because it did not fit the gameplay well. I also changed the showcase so the camera stays still while only the selected vehicle spins, which makes switching vehicles look smoother.

I added sounds for menu switching, selecting options, countdown ticks, checkpoints, winning, pausing, and vehicle engines. I matched vehicle sounds from the sound folders as much as possible, then organized the added audio into the correct UI and vehicle sound folders. I also added a pause menu with Escape, including Resume and Return to Main Menu, so the player can stop the race without restarting the whole game.

Adding another map with lighting for the vehicles and some improvements

After the desert map, I added a second map with a forest theme. The forest map had its own issues. At first it did not always render correctly and sometimes Unity showed that no camera was rendering, so I fixed the map switching and camera setup so the forest map could be selected and played. I also added the forest map to the Choose Map menu with its own option and picture.

The forest map needed a finish line and spawn point too. I moved the spawn point to match the finish line, and when the finish line position changed, I updated the spawn point again so the car starts exactly there and faces the same direction. I also separated the best lap time for each map, so the desert and forest records do not overwrite each other.

Lighting was another big part of the forest map. Since the forest has a darker mood, I added front lights and back lights for vehicles only on that map. The first headlights looked too sharp and unrealistic, so I softened them by lowering the intensity, widening the light angle, and adding smoother glow effects. I had some problems with adjusting the lights on some vehicles, so some of them have their headlights and tail lights are off their right place. I also worked on the forest sky and lighting so it fits the night style better, while the desert keeps its brighter racing feel.

Near the end, I cleaned the project structure. Screenshots were sorted into folders, the skybox demo and the cars were moved into ThirdParty, input files were moved into Settings, recovery files were placed in a backup folder, and the forest map builder was moved into the forest map folder. Overall, I learned a lot about Unity scenes, prefabs, scripts, UI, sounds, lighting, materials, GitHub commits, and how small problems can slowly turn into better systems when they are fixed one by one.

Controls

W\A\S\D: to move
Space: to drift
R: to restart
Esc: to pause the game
