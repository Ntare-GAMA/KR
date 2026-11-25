# Kigali Rush VR: Rush Week story script

The game reads the conversations and quiz from `Assets/Resources/Story/story_script.json`. This document is the readable version. If you change a line here, change it in the JSON too, or ask for the JSON to be regenerated.

**Content rules.**
- Every person and every line is fictional and written by the team.
- Road-rule facts come only from Law n° 014/2026, as cited.
- The quiz questions stay `draft` until a reviewer approves them.
- The Kinyarwanda phrases need a native speaker's check before release. They are marked *(review)* below.

---

## Premise

You are a first-year student at the African Leadership University campus. You live in Kibagabaga, and your group's summative is due on Wednesday. Rush Week is three days of deadlines across Kimironko and Kibagabaga. You walk, or you drive your own car, and you learn to drive the way it is done in Rwanda while you do it. Every session starts in the morning.

## Characters

| Character | Who they are | Where | Role |
|---|---|---|---|
| **Maman Fils** | Mother of your friend Fils. Warm and teasing, and keen on good manners. | Her house in Kibagabaga, about 350 m from yours | Tea on Monday, a thank-you on Wednesday |
| **Aline** | Your classmate and group leader. Fast-talking and organised. | The ALU campus gate | Greets you at class, chats about the group |
| **Jean-Paul** | Campus librarian. Calm, with a dry humour. | Near the campus gate | Reading-list mission |
| **Officer Mukamana** | Traffic police officer. Firm but kind. | A road in Kibagabaga | Road lesson and quiz |
| **Mzee Gakuba** | Fabric seller at Kimironko Market. A joker who loves bargaining. | Beside Kimironko Market | Fabric for the poster |
| **Fils** | Your friend, Maman Fils's son. He is mentioned but not seen. | | |

## Rush Week missions

| # | Day | Mission | Type | Where | What completes it |
|---|---|---|---|---|---|
| 1 | Monday | **Make the 9:00 class** | Arrive | ALU campus gate | Reach the gate. Starts at home. Aline: "You made it! Sit with us, the lecture is starting." |
| 2 | Monday | **Find a book for the summative** | Talk | Jean-Paul, campus | Finish the conversation with Jean-Paul |
| 3 | Monday | **Tea with Maman Fils** | Talk | Maman Fils's house | Finish the tea conversation |
| 4 | Tuesday | **A road lesson with Officer Mukamana** | Talk and quiz | Kibagabaga road | Listen, then get at least 2 of 3 questions right (talk again to retry) |
| 5 | Tuesday | **The parking test** | Park in bay | Marked bay near the campus gate | Stop inside the white bay, facing along the road, in **P** |
| 6 | Tuesday | **Fabric from Mzee Gakuba** | Talk | Kimironko Market | Buy the fabric (choose blue or yellow) |
| 7 | Tuesday | **Group meeting on campus** | Arrive and stay | ALU campus | Stay 30 s with your group |
| 8 | Wednesday | **A photo for the poster** | Photo | ALU campus | Take a photo with the campus in the middle of the frame, from no more than about 150 m away |
| 9 | Wednesday | **Submit before the deadline** | Arrive and stay | ALU campus | Stay 15 s to submit |
| 10 | Wednesday | **Thank Maman Fils** | Talk | Maman Fils's house | Finish the thank-you conversation |

Every mission has a par time. Breaking a road rule adds time. Learners get a lesson instead of a fine until they have driven 3 km cleanly. Completing a mission gives 100 XP, plus 25 XP per star, the first time only.

---

## Conversations

Choices are shown as **→**. `[flag]` means the game remembers that choice for later.

### Maman Fils: tea (`mf_tea`, mission 3)

> **Maman Fils:** Mwaramutse! You found the house. Fils told me his friend from ALU was coming. Come in, come in.
> → "Mwaramutse, Maman Fils. Amakuru?" `[greeted_maman_kinyarwanda]` *(review)*
>   **Maman Fils:** Ni meza! You are learning Kinyarwanda, good. Sit down, the icyayi is ready. *(review)*
> → "Good morning! Thank you for having me."
>   **Maman Fils:** You are welcome here. Next time, try 'Mwaramutse'; it makes an old woman happy. Sit down, the icyayi is ready.
>
> **Maman Fils:** So, how is Fils doing on campus? He never tells me anything.
> → "He works hard. We are in the same group for the summative."
>   **Maman Fils:** A group project! Then you must all finish it together. Drink, before it gets cold.
> → "He is always the first one in class." `[praised_fils]`
>   **Maman Fils:** The first? That boy? You are being kind. Drink, before it gets cold.
>
> **Maman Fils:** You came by car? Drive carefully on the way back. The roads near the market are busy at this hour.
> → "I will. Murakoze cyane for the tea." `[thanked_maman_kinyarwanda]` *(review)*
> → "Thank you, I will be careful."
>
> **Maman Fils:** Come back when the summative is finished and tell me how it went.

### Maman Fils: thank-you (`mf_thanks`, mission 10)

> **Maman Fils:** Mwiriwe! You look tired, but happy. Is it finished? *(review)*
> → "We submitted it this afternoon, before the deadline."
>   **Maman Fils:** Before the deadline! That is how it should be. I am proud of you both.
> → "Finished! Fils took the photo for the poster... well, I did."
>   **Maman Fils:** Ha! Then the poster will be beautiful. I am proud of you both.
>
> **You:** Thank you for the tea on Monday. It helped.
> **Maman Fils:** The door is always open. Now go and rest, and drive slowly.

### Maman Fils: everyday (`mf_chat`)

> **Maman Fils:** Muraho! Are you looking for Fils? He is on campus all day this week. *(review)*
> **Maman Fils:** Greet the neighbours on your way. A street is friendlier when people say hello.

### Jean-Paul: reading list (`jp_library`, mission 2)

> **Jean-Paul:** Good morning. You look like someone with a summative due on Wednesday.
> → "How did you know?"
>   **Jean-Paul:** Everyone who walks in this week has the same face. Let me help.
> → "Yes. Where should our group start reading?"
>
> **Jean-Paul:** Start with the two books on your module's reading list, not the whole shelf. Read the summary chapters first.
> **Jean-Paul:** Read in short blocks and write three lines of notes after each one. Share the notes with your group tonight.
> → "Short blocks and notes. Got it." `[study_plan]`
>   **Jean-Paul:** Good. Come back if you get stuck. And do not drive and read at the same time!
> → "Can I borrow both books today?"
>   **Jean-Paul:** One today, one tomorrow, so your group can share. Now go, the clock is running.

### Jean-Paul: everyday (`jp_chat`)

> **Jean-Paul:** The quiet room is free this afternoon if your group needs it.

### Officer Mukamana: road lesson (`off_lesson`, mission 4)

> **Officer Mukamana:** Mwaramutse. I am Officer Mukamana. You are here for the road-safety talk for new drivers? Good. It is short, then three questions.
> → "Yes, I am ready."
> → "Will this be on a test?"
>   **Officer Mukamana:** Three questions, right after the talk. Listen well.
>
> **Officer Mukamana:** First: in Rwanda, vehicles drive on the right-hand side of the road. Keep right, always. *(Law 014/2026, Art. 4)*
> **Officer Mukamana:** Second: when an officer is directing traffic, the officer's instructions come before the signs and the traffic lights. *(Art. 6(3))*
> **Officer Mukamana:** Third: if an officer signals you to stop, you stop. Refusing to stop is an offence with a heavy fine and even prison. *(Art. 39)*
> **Officer Mukamana:** Now, the questions. *(quiz below)*
> **Officer Mukamana:** Thank you. Drive like that every day and you will not need to see me again, except to say hello.

### Officer Mukamana: everyday (`off_chat`)

> **Officer Mukamana:** Seatbelt on, speed down, and stop at the white line on red. That is all I ask.

### Mzee Gakuba: fabric (`gk_market`, mission 6)

> **Mzee Gakuba:** Muraho, young one! You came just in time; I am closing early today. What are you looking for?
> → "Fabric for our group's poster display."
>   **Mzee Gakuba:** A poster? Then you want colour that can be seen from far away. This blue, or this yellow?
> → "Muraho, Mzee! Fabric, please, something bright." `[greeted_gakuba]` *(review)*
>   **Mzee Gakuba:** Bright! You came to the right stall. This blue, or this yellow?
>
> **Mzee Gakuba:** Choose one.
> → "The blue one." `[fabric_blue]`
> → "The yellow one." `[fabric_yellow]`
>
> **Mzee Gakuba:** Good choice. For a student, a fair price. Carry it carefully, the market is crowded.
> **You:** Murakoze, Mzee! *(review)*
> **Mzee Gakuba:** Come back when you are rich and famous. Ha!

### Mzee Gakuba: everyday (`gk_chat`)

> **Mzee Gakuba:** The stall opens early tomorrow. Come before the midday crowd.

### Aline: everyday (`al_chat`)

> **Aline:** Hey! Did you see the group chat? We meet on campus before the deadline.
> → "I will be there."
>   **Aline:** Great. Bring the fabric if you have it.
> → "Can someone else take the poster photo?"
>   **Aline:** No way, you have the steady hands. Stand back so the whole building fits!

---

## Road-rule quiz

| Id | Question | Answer | Source |
|---|---|---|---|
| `q_right_side` | On which side of the road do vehicles drive in Rwanda? | The right-hand side | Law n° 014/2026, Art. 4 |
| `q_officer_first` | A traffic light is green, but an officer directing traffic signals you to stop. What do you do? | Stop: the officer's instructions come first | Law n° 014/2026, Art. 6(3) |
| `q_refuse_stop` | What happens if a driver refuses to stop when an officer signals them? | It is an offence: a fine of FRW 400,000 to 700,000 and 3 to 6 months' imprisonment | Law n° 014/2026, Art. 39 |

## Kinyarwanda phrases used (native-speaker review needed)

| Phrase | Meaning used in the game |
|---|---|
| Mwaramutse | Good morning |
| Mwiriwe | Good afternoon or evening |
| Muraho | Hello |
| Amakuru? | How are you? |
| Ni meza | (I am) well |
| Murakoze (cyane) | Thank you (very much) |
| Icyayi | Tea |
| Mzee | A respectful word for an older man |

## Places to discover

The game builds the list from the OpenStreetMap places near each sector centre. It uses only public places: markets, parks, schools, universities, health centres, bus stations, sports grounds and places of worship. It also includes ALU, Kimironko Market and Maman Fils's house. Private businesses such as restaurants, bars, hotels and shops are left out.

Each place has a journal card that starts empty ("Card not written yet"). To fill one, give the text and its source, and it will be added to `Assets/World/Story/Places.asset` as `draft` until reviewed. The **Kimironko tour** starts at the market and then visits the nearest Kimironko places, up to six stops.

## The living city (named places, businesses, police, accidents)

**Names.** Every signboard, satnav destination and house-number plaque comes from OpenStreetMap through `Tools/gis/extract_named.py`, which writes `named_unity.json`. Names are exactly as mapped, and nothing is invented. Private homes have no names in OSM, so they are not named in the game. Where OSM has a house number, the house gets a plaque (for example "No. 7, KG 36 Street"), and every named street has name blades at its ends.

The extract leaves out:

- police, government, military, diplomatic and public-administration sites;
- MTN-branded places, because the game uses no MTN branding.

TODO (legal): business names appear as plain text with no logos. Confirm before release that showing them is acceptable.

**Businesses.** About 140 businesses near the sector centres have a member of staff at the door. Walk up and press Enter (or X in VR) to talk to them; each kind of business has its own conversation. Customers stand around, and the staff are there only during opening hours. These hours are generic game hours by kind (for example, cafés 07:00 to 22:00 and banks 08:00 to 17:00), not the places' real hours.

| Conversation | Who | What you can do |
|---|---|---|
| `svc_food` | Cafés, restaurants, bakeries, the ALU canteen | Order tea, a plate or water |
| `svc_bar` | Bars | Order a soda, ask about football |
| `svc_shop` | Shops, supermarkets, markets | Buy household things, phone credit |
| `svc_pharmacy` | Pharmacies | Basic items, a prescription (no medical advice) |
| `svc_clinic` | Clinics, hospitals (front desk) | Information, visiting |
| `svc_bank` | Banks | Student account, cash machine |
| `svc_hotel` | Hotels, guest houses | Reception |
| `svc_salon` | Hair salons | A trim |
| `svc_garage` | Fuel stations, garages, car washes | Fuel, tyre check |
| `svc_office`, `svc_school`, `svc_gym`, `svc_alu` | Offices, schools, gyms, ALU front desk | Directions, information |
| `police_chat` | Traffic officers, foot patrols | Road-safety advice |
| `acc_driver` | The other driver after a crash | Apologise, argue, exchange details |

**Accidents.** If you hit another car, it stops with a long horn blast. Its driver gets out and comes to you, shouting (lines in a bubble above their head), and the cars stuck behind hoot and their drivers call out.

- **Settling it:** stop, shift to P, get out and talk to the driver (`acc_driver`). They drive off afterwards.
- **Driving away:** this sets the flag `left_crash`.

Flags set: `crash`, `crash_settled`, `acc_apologised`, `acc_argued`, `acc_details_exchanged`.

**Police.** Traffic officers in hi-vis vests direct the busiest junctions near each sector, and pairs of officers walk a stretch of footpath. The uniform is a stand-in with no insignia.

TODO (verify): the colours of the Rwanda National Police traffic uniform and of Kigali street signs.

**Pedestrians.** People cross at the zebra crossings of signal-controlled junctions, only while the cars on that road have red and there is time to reach the other side.

Kinyarwanda in the new lines (review needed): Muraho, Murakoze.
