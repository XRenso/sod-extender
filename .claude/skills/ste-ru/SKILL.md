---
name: ste-ru
description: Write every reply to the user in the style of Simplified Technical English (ASD-STE100), adapted to Russian. Use for ALL user-facing text in this project — reports, answers, questions, summaries.
---

# STE style for Russian replies

Source: rules of ASD-STE100 (Simplified Technical English), paraphrased from the community skill
github.com/nuelcyoung/asd-ste100 (MIT, unofficial) and adapted to Russian. The STE dictionary is English, so
apply the rules, not the word list.

## Rules
1. **Meaning first.** Never make a sentence simpler if this changes the meaning. Restructure it or define the term.
2. **Short sentences.** Instruction: max 20 words. Description: max 25 words. One idea per sentence.
3. **One instruction per sentence.** Write commands in the imperative: «Запустите игру.» Not: «Нужно бы запустить игру, а потом…».
4. **Active voice.** «Мост загружает сборку.» Not: «Сборка загружается мостом.»
5. **One word = one meaning.** Use the same term for the same thing in all the text (мост, мод, плагин, сохранение). Do not use synonyms for variety.
6. **Simple, concrete words.** No idioms, metaphors, slang, filler, emotion, hedging («как бы», «вроде», «в принципе»).
7. **Technical names stay as they are**: file names, functions, API, keys (`sod.py`, `Player.Instance`, Q, ЛКМ).
8. **Lists for steps and for 3+ facts.** Numbered list for a sequence. Bullet list for parallel facts. Max 3 levels of structure.
9. **Paragraphs:** one topic, max 6 sentences.
10. **Give the result first**, then the cause, then the next step.
11. **Warnings and conditions** go before the instruction they apply to: «Если клиенты упали, остановите тест.»
12. **No vague words**: «быстро», «немного», «примерно» only with a number (0.67 с, 2 кадра).

## Check before you send
- Each sentence ≤ 20 words (instruction) / ≤ 25 words (description)?
- Active voice, one idea per sentence?
- Same term for the same thing everywhere?
- No idioms, no filler?
