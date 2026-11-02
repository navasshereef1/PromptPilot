# Prompt techniques: what the evidence says

Researched 2 Nov 2026 from research papers, official docs, GitHub, X, LinkedIn, Reddit and Quora.
Social posts are anecdotes unless a study is named, so they count as "worth trying", not as proof.

## Strong evidence: built into Engineer

| Technique | Evidence | Where in the app |
|---|---|---|
| Put long material first and the question last | Anthropic: up to 30% better on complex, multi-document inputs | Engineer, *Long document* template |
| Be specific about format, length and audience | Official guides, and the most consistent community advice | Engineer, Improve |
| Explain why you want it | Anthropic best practices, LinkedIn reports | Engineer |
| Give 3 to 5 examples when the format matters | Anthropic best practices | *Teach by examples* |
| Say what to do, not what to avoid | Anthropic best practices, FLUX docs | Engineer, image modes |
| Quote the document before answering | Anthropic best practices | *Long document* template |
| Repeat the prompt on fast models | Google Research: won 47 of 70 tests with 0 losses on models without reasoning; neutral with reasoning. Costs more input tokens | *Repeat twice* mode |
| Static content first, for caching | OpenAI: up to 90% lower input cost and 80% lower latency on cache hits, API only | Long context habit |

## Mixed or weak evidence

| Technique | What is known | What the app does |
|---|---|---|
| "You are a world-class expert" | Wharton: personas generally did not improve accuracy, and mismatched ones can hurt. They can still change tone | Engineer adds a role only for tone or perspective |
| Emotional appeals ("this is vital for my career") | The 2023 EmotionPrompt paper reported gains on older models. I found no independent replication on current ones | Not used |
| Politeness or rudeness | Wharton: inconsistent and unpredictable | Not used |
| Chain of thought ("think step by step") | Helps some models and tasks. Reasoning models already do it, and fancier variants may age badly | Only when useful, as steps |
| "Grade and improve my prompt" loops | Popular on Reddit, anecdotal | *Improve my prompt with me* |
| Compressed "caveman" style | Up to 75% fewer output tokens claimed by the project. One guide found it about 10% dearer on a single question and about 39% cheaper across long cached sessions | *Cost saver*, *Keep answers short* |

## Myths

| Claim | Finding |
|---|---|
| Threatening or tipping the AI improves answers | Wharton: no significant effect on benchmarks |
| One magic prompt or "cheat code" | No evidence. Mollick and others: specific context and iteration matter more |
| Copying leaked system prompts into your chats | They are written for products with tools. Learn the patterns: clear sections, explicit rules, identity first |

## Reverse psychology and "psychological" prompts

The useful version isn't manipulating the AI. It is getting past its habit of agreeing with you:

- **Argue against me:** ask for the strongest honest case against your view (*Argue against me*, *Pushback* mode).
- **Do the opposite:** ask when the opposite of your instinct is right (*Do the opposite*).
- **Pre-mortem:** assume the plan failed, then explain why (*Pre-mortem*).
- **Wrong assumptions first:** Ethan Mollick's one-line fix (*List wrong assumptions first*).
- **Attack your own answer:** answer, critique, improve (*Answer, attack, improve*, *Critic* mode).

Persuasion tricks meant to get past an AI's safety rules are deliberately left out.

## Reverse engineering

- **From content:** paste work you admire and get a reusable prompt with blanks (*Reverse prompt* mode). Use it to learn structure, not to copy.
- **From an image:** attach it in a chat that can see images (*Reverse engineer an image* template).
- **Let the AI interview you:** have it ask questions first (*Ask me first* mode). This often beats writing a long prompt yourself.

## Faster and cheaper

1. Choose the right model first. This changed speed more than any wording: Fix took 0.14 s on Groq's Qwen against 0.6 to 5.5 s on Gemini.
2. Send only relevant context, and keep project notes short.
3. Ask for short output with no preamble or recap.
4. In long sessions, keep the opening stable so caching works, and start a new chat for a new topic.

## Image prompts

| Rule | Source |
|---|---|
| Write full sentences describing a scene, not keyword lists | Google, BFL and several 2026 guides |
| Photoreal pattern: shot type, subject, setting, light, angle, lens | Google Gemini docs |
| Put exact text in quotes, with its position and style | Google Gemini docs |
| Edit in steps: "Update this image to X. Do not change any other elements." | Google Gemini docs |
| Change one thing at a time, and use reference images for style | Several guides |
| FLUX.2 has no negative prompts: say "sharp focus", not "not blurry" | Black Forest Labs help centre |
| Midjourney: use `--no` for exclusions, since "no fruit" in the text adds fruit. `--sref` matches a style | Third-party docs, so check against Midjourney's own |

Not found: OpenAI's own image prompting guide, so *Image: any model* relies on the general rules above.

## Sources

- [Anthropic prompting best practices](https://platform.claude.com/docs/en/build-with-claude/prompt-engineering/claude-prompting-best-practices)
- [Wharton: threats and tips](https://arxiv.org/pdf/2508.00614)
- [Wharton: expert personas](https://arxiv.org/pdf/2512.05858)
- [Mollick on the first prompting report](https://x.com/emollick/status/1897046511101599797)
- [Mollick: wrong assumptions](https://twitter.com/emollick/status/1804654718893133990)
- [Google: prompt repetition](https://arxiv.org/pdf/2512.14982)
- [EmotionPrompt](https://arxiv.org/abs/2307.11760)
- [OpenAI prompt caching](https://developers.openai.com/api/docs/guides/prompt-caching.md)
- [Caveman analysis](https://betterstack.com/community/guides/ai/caveman-llm/)
- [Gemini image generation](https://ai.google.dev/gemini-api/docs/image-generation)
- [FLUX.2 negative prompts](https://help.bfl.ai/articles/7734566352-does-flux-2-support-negative-prompting)
- [Midjourney guide (third party)](https://evolink.ai/docs/en/api-manual/image-series/midjourney/midjourney-v8-2-prompt-guide.md)
- [Reverse prompting](https://mysocial.io/blog/reverse-prompting-chatgpt)
- [Opposite advice prompt](https://www.tomsguide.com/ai/the-reverse-advice-prompt-that-makes-chatgpt-give-next-level-responses)
- [Leaked prompts overview](https://www.augmentcode.com/learn/leaked-ai-system-prompts-github)
- [DAIR.AI guide on GitHub](https://github.com/dair-ai/prompt-engineering-guide)
- [r/PromptEngineering archive](https://reddit.sentinel-team.org/subreddits/PromptEngineering/snapshots/2026-05-26T08%3A44%3A25.798102Z)
- [LinkedIn: favourite prompt hack](https://www.linkedin.com/posts/ruben-hassid_this-is-my-favorite-chatgpt-prompt-hack-activity-7324749642196221954-G2aF)
- [Quora: writing better prompts](https://www.quora.com/How-do-I-write-better-prompts-for-ChatGPT-to-improve-the-results)
