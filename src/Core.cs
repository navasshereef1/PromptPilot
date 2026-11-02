using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

class Provider
{
    public string Name, Url, Model, KeyEnc, KeyPage, Note;
    public bool Enabled = true;
    [ScriptIgnore] public DateTime CooldownUntil;

    bool IsLocal() { return Url.Contains("localhost") || Url.Contains("127.0.0.1"); }
    // Local servers such as Ollama need no key.
    public bool Usable() { return Enabled && (!string.IsNullOrEmpty(KeyEnc) || IsLocal()); }
    public string Key() { return string.IsNullOrEmpty(KeyEnc) ? "" : Settings.Decrypt(KeyEnc); }
}

class Mode
{
    public string Id, Name, Instructions, Hotkey = "";
    public bool Pinned, UseContext, Preview, Builtin;
    public double Temperature = 0.3;
    public override string ToString() { return Name; }
}

class Project
{
    public string Name = "New project", Notes = "", Keywords = "";
    public List<string> Files = new List<string>();
    public override string ToString() { return Name; }
}

class Template
{
    public string Name = "New template", Category = "General", Body = "";
    public int Uses;
    public override string ToString() { return Name; }
}

class Settings
{
    public static string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PromptPilot");
    static string FilePath { get { return Path.Combine(Dir, "settings.json"); } }

    public bool Enabled = true, ShowEverywhere = false;
    public int Version = 0;
    public int X = -1, Y = -1;                 // version 1 position, migrated once
    public List<Provider> Providers;

    public string Style = "Bar", Placement = "Free", Theme = "Dark", Accent = "#4C9DFF";
    public int Opacity = 100, Size = 1, HedgeMs = 2500, HistoryDays = 90;
    public Dictionary<string, int[]> Positions = new Dictionary<string, int[]>();
    public int FollowDx = 0, FollowDy = -130;
    public List<string> Sites;

    public List<Mode> Modes;
    public List<Project> Projects = new List<Project>();
    public List<Template> Templates;
    public string AboutMe = "", ActiveProject = "";
    public bool AutoProject = true, ReadChat = false, AskFirst = false;

    public string Guard = "Mask";              // Mask, Warn, Off
    public bool HistoryOn = true, SelectionFirst = true, WarmUp = true, Cache = true;
    public string HkUndo = "Ctrl+Alt+Z", HkPalette = "Ctrl+Alt+T", HkStudio = "Ctrl+Alt+P", HkToggle = "Ctrl+Alt+B";

    static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }; }
    public static string ToJson(object o) { return Json().Serialize(o); }
    public static T FromJson<T>(string s) { return Json().Deserialize<T>(s); }

    public static Settings Load()
    {
        Settings s = null;
        try { s = FromJson<Settings>(File.ReadAllText(FilePath)); }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (Exception e)
        {
            Log("settings unreadable: " + e.Message);
            try { File.Copy(FilePath, FilePath + ".broken", true); } catch { }
        }
        s = s ?? new Settings();
        s.Fill();
        return s;
    }

    void Fill()
    {
        if (Providers == null) Providers = new List<Provider>();
        foreach (var p in Defaults.Providers())
        {
            var have = Providers.FirstOrDefault(x => x.Name == p.Name);
            if (have == null) Providers.Add(p);
            else { have.KeyPage = p.KeyPage; have.Note = p.Note; }
        }
        if (Modes == null) Modes = new List<Mode>();
        foreach (var m in Defaults.Modes())
            if (!Modes.Any(x => x.Id == m.Id)) Modes.Add(m);
        if (Templates == null) Templates = Defaults.Templates();
        if (Sites == null) Sites = Defaults.Sites();
        if (X >= 0 && !Positions.ContainsKey("Bar")) Positions["Bar"] = new[] { X, Y };
        Upgrade();
    }

    // Runs once per version, so later edits to modes and models are never overwritten.
    void Upgrade()
    {
        const int Current = 2;
        if (Version >= Current) return;
        var groq = Providers.FirstOrDefault(p => p.Name == "Groq");
        if (groq != null && groq.Model == "llama-3.3-70b-versatile") groq.Model = Defaults.Providers()[0].Model;
        foreach (var d in Defaults.Modes())
        {
            var m = FindMode(d.Id);
            if (m == null || !m.Builtin) continue;
            m.Instructions = d.Instructions;
            m.Temperature = d.Temperature;
        }
        foreach (var t in Defaults.Templates())
            if (!Templates.Any(x => x.Name == t.Name)) Templates.Add(t);
        Version = Current;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, ToJson(this));
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath);
        }
        catch (Exception e) { Log("settings save failed: " + e.Message); }
    }

    public Mode FindMode(string id) { return Modes.FirstOrDefault(m => m.Id == id); }
    public Project Active() { return Projects.FirstOrDefault(p => p.Name == ActiveProject); }

    public static string Encrypt(string plain) { return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser)); }
    public static string Decrypt(string enc) { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(enc), null, DataProtectionScope.CurrentUser)); }

    // Errors only. Prompt text never goes in here.
    public static void Log(string msg)
    {
        try { Directory.CreateDirectory(Dir); File.AppendAllText(Path.Combine(Dir, "log.txt"), DateTime.Now.ToString("s") + " " + msg + "\r\n"); }
        catch { }
    }
}

static class Defaults
{
    public static List<string> Sites()
    {
        return new List<string> { "chatgpt.com", "chat.openai.com", "claude.ai", "gemini.google.com", "aistudio.google.com", "perplexity.ai",
            "copilot.microsoft.com", "chat.deepseek.com", "grok.com", "poe.com", "chat.mistral.ai", "meta.ai", "notebooklm.google.com", "chat.qwen.ai" };
    }

    public static List<Provider> Providers()
    {
        return new List<Provider> {
            new Provider { Name = "Groq", Url = "https://api.groq.com/openai/v1/chat/completions", Model = "qwen/qwen3.8-27b", KeyPage = "https://console.groq.com/keys", Note = "Fastest in testing. About 1,000 output tokens a minute free, then the next provider takes over." },
            new Provider { Name = "Cerebras", Url = "https://api.cerebras.ai/v1/chat/completions", Model = "gpt-oss-120b", KeyPage = "https://cloud.cerebras.ai/", Note = "Fastest listed speed. Free tier." },
            new Provider { Name = "Gemini", Url = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", Model = "gemini-3.5-flash-lite", KeyPage = "https://aistudio.google.com/apikey", Note = "Free tier. Google may use prompts to improve products." },
            new Provider { Name = "OpenRouter", Url = "https://openrouter.ai/api/v1/chat/completions", Model = "openrouter/free", KeyPage = "https://openrouter.ai/keys", Note = "Many free models, about 50 requests a day." },
            new Provider { Name = "Mistral", Url = "https://api.mistral.ai/v1/chat/completions", Model = "mistral-small-latest", KeyPage = "https://console.mistral.ai/api-keys", Note = "Free experiment plan." },
            new Provider { Name = "Ollama (local)", Url = "http://localhost:11434/v1/chat/completions", Model = "llama3.2", Enabled = false, KeyPage = "https://ollama.com/download", Note = "Private and offline, but slow without a GPU." },
        };
    }

    public static List<Mode> Modes()
    {
        return new List<Mode> {
            new Mode { Id = "fix", Name = "Fix", Builtin = true, Pinned = true, Hotkey = "Ctrl+Alt+1", Temperature = 0.1,
                Instructions = "Correct spelling, grammar, punctuation and obvious wrong words. Keep the meaning, tone, length and details exactly. Add nothing." },
            new Mode { Id = "improve", Name = "Improve", Builtin = true, Pinned = true, UseContext = true, Hotkey = "Ctrl+Alt+2",
                Instructions = "Make the draft clearer and more specific so an AI gives a better answer. Keep the user's voice and write natural sentences without headings. Make the goal explicit, remove vagueness and repetition, and state the wanted format or depth when the draft implies it. Do not invent facts. Keep it under about twice the original length." },
            new Mode { Id = "engineer", Name = "Engineer", Builtin = true, Pinned = true, UseContext = true, Hotkey = "Ctrl+Alt+3", Temperature = 0.4,
                Instructions = "Act as an expert prompt engineer. First silently analyse the task type, the real goal, the audience and what would make an answer fail. Then write a ready-to-send prompt using these rules. State the goal and why it matters. Give the context before the task; if there is long material, place it first and the request last. Say exactly what a great answer looks like: format, length, depth and audience. Phrase instructions as what to do rather than what to avoid. Include one short example only when the format matters. Tell the AI to state its assumptions, separate facts from guesses and say when it does not know. For important decisions, ask it to check its answer against the success criteria before finishing. Add a role only when it changes tone or perspective, not as a claim of expertise. Never add threats, tips, flattery or emotional pressure. Scale the length to the task: a simple request stays short. Keep every fact from the draft. Never invent facts: use [square bracket placeholders] for essential details only the user knows." },
            new Mode { Id = "shorten", Name = "Shorten", Builtin = true, Temperature = 0.2,
                Instructions = "Make the draft as short as possible while keeping every requirement and fact. Remove filler and repetition." },
            new Mode { Id = "detail", Name = "Add detail", Builtin = true, UseContext = true,
                Instructions = "Expand the draft by adding the useful specifics an AI would need: scope, depth, audience, format and edge cases. Use [square bracket placeholders] for details only the user knows. Do not invent facts." },
            new Mode { Id = "english", Name = "To English", Builtin = true, Temperature = 0.2,
                Instructions = "Translate the draft into clear natural English, including mixed-language or transliterated text. Keep the meaning and all details. This instruction overrides keeping the draft's language." },
            new Mode { Id = "code", Name = "Code prompt", Builtin = true, UseContext = true,
                Instructions = "Rewrite the draft as a precise request to a coding AI: the language and stack, current behaviour, wanted behaviour, constraints, files or code involved, and how to verify the result. Use [square bracket placeholders] for details only the user knows. Do not invent facts." },
            new Mode { Id = "tokens", Name = "Cost saver", Builtin = true, Temperature = 0.1,
                Instructions = "Rewrite the draft to use as few tokens as possible while keeping every requirement, name, number and condition, including every negation. Remove politeness, filler and repetition. Put any long pasted material first and the request last. End with a short instruction to answer concisely with no preamble and no recap." },
            new Mode { Id = "critic", Name = "Critic", Builtin = true, UseContext = true,
                Instructions = "Rewrite the draft so the AI must test its own work: keep the request, then ask it to give its answer, list the three weakest points or assumptions in that answer, and finish with an improved final version. Add clear success criteria for the check, using only what the draft implies." },
            new Mode { Id = "pushback", Name = "Pushback", Builtin = true, UseContext = true,
                Instructions = "Rewrite the draft so the AI will not simply agree. Keep the request, then ask it to identify the assumptions behind the request, give the strongest case against the user's current view or plan, say what evidence would change its mind, and only then give a recommendation. Tell it to prefer honest disagreement over politeness." },
            new Mode { Id = "askfirst", Name = "Ask me first", Builtin = true,
                Instructions = "Keep the draft's request and add an instruction telling the AI not to answer yet: it should first ask up to 5 short questions, one at a time, about whatever would most change the result, then answer once the user has replied." },
            new Mode { Id = "reverse", Name = "Reverse prompt", Builtin = true, Temperature = 0.2,
                Instructions = "This task overrides the instruction to rewrite the draft. Treat the draft as a finished piece of work the user admires. Write a reusable prompt that would produce new work with the same structure, tone, length, techniques and audience. Describe those qualities specifically. Replace the piece's specific topic and facts with {{variables}}, and do not copy its sentences." },
            new Mode { Id = "repeat", Name = "Repeat twice", Builtin = true, Temperature = 0.0,
                Instructions = "Output the draft exactly as written, then a blank line, then the same draft exactly as written again. Change nothing else. Use this for fast models without a reasoning step." },
            new Mode { Id = "image", Name = "Image: any model", Builtin = true, Temperature = 0.6,
                Instructions = "Rewrite the draft as an image generator prompt in full natural sentences describing one clear scene, not a keyword list. Cover the subject and action, setting, composition and camera angle, lens if photographic, lighting, style, colour palette and mood. Put any text that must appear in double quotes, with its position and style. State the aspect ratio when the draft implies one. Describe wanted qualities positively, such as 'sharp focus' instead of 'not blurry'. Keep everything the draft specifies, and do not add people or brands it does not mention." },
            new Mode { Id = "image_photo", Name = "Image: photoreal", Builtin = true, Temperature = 0.5,
                Instructions = "Rewrite the draft as a photorealistic image prompt in this form: A photorealistic [type of shot] of [subject] in [setting]. [Description of the light]. Shot from [camera angle] with [lens type]. Then add details of texture, colour and mood in one or two sentences. Keep everything the draft specifies. Use [square bracket placeholders] only for details that cannot be sensibly chosen." },
            new Mode { Id = "image_mj", Name = "Image: Midjourney", Builtin = true, Temperature = 0.6,
                Instructions = "Rewrite the draft as a concise Midjourney prompt: the subject first, then setting, style, lighting and mood in short natural phrases. Put unwanted elements only in a --no parameter at the end, as a comma-separated list, because writing 'no x' in the prompt text adds x. End with --ar W:H when the draft implies a shape. Add --sref [style reference URL] only if the draft mentions matching a style." },
            new Mode { Id = "image_flux", Name = "Image: FLUX", Builtin = true, Temperature = 0.6,
                Instructions = "Rewrite the draft as a FLUX prompt in plain prose, as if describing a photograph or artwork to a person: subject first, then setting, composition, lighting, style and mood. FLUX.2 does not use negative prompts, so turn every exclusion into what should be seen instead, such as 'clean empty background' instead of 'no clutter'. Keep everything the draft specifies." },
            new Mode { Id = "image_edit", Name = "Image: edit", Builtin = true, Temperature = 0.2,
                Instructions = "Rewrite the draft as an instruction to edit an existing image: Update this image to [the change]. Do not change any other elements of the image. If the draft asks for several changes, list them as separate numbered steps so they can be applied one at a time." },
        };
    }

    public static List<Template> Templates()
    {
        return new List<Template> {
            new Template { Name = "Explain simply", Category = "Learning", Body = "Explain {{topic}} to someone who knows {{background}}. Start with a one-sentence summary, then use one concrete example. Finish with the most common misunderstanding." },
            new Template { Name = "Summarise a document", Category = "Writing", Body = "Summarise the text below for {{audience}}. Give the 5 most important points as bullets, then list any decisions or actions needed.\n\nText:\n{{text}}" },
            new Template { Name = "Write an email", Category = "Writing", Body = "Write an email to {{recipient}} about {{subject}}. The goal is {{goal}}. Tone: {{tone}}. Keep it under {{length}} words and end with a clear next step." },
            new Template { Name = "Debug this", Category = "Coding", Body = "I am using {{stack}}. This is what happens: {{actual}}. This is what should happen: {{expected}}.\n\nCode:\n{{code}}\n\nFind the root cause first, explain it in two sentences, then give the smallest fix." },
            new Template { Name = "Review my code", Category = "Coding", Body = "Review this {{language}} code for bugs, security problems and needless complexity. List issues by severity, each with the fix.\n\n{{code}}" },
            new Template { Name = "Compare options", Category = "Decisions", Body = "Help me choose between {{options}} for {{purpose}}. My priorities are {{priorities}}. Compare them in a table, then recommend one and say what would change your mind." },
            new Template { Name = "Marketing copy", Category = "Marketing", Body = "Write {{format}} for {{product}}, aimed at {{audience}}. Their main problem is {{problem}}. Give 3 versions with different angles, each with a clear call to action." },
            new Template { Name = "Plan a project", Category = "Planning", Body = "Create a step-by-step plan to {{goal}} by {{deadline}}. I have {{resources}}. Show milestones, the first action for today, and the biggest risks." },

            new Template { Name = "Ask me questions first", Category = "Technique", Body = "I want to {{goal}}. Before you answer, ask me up to 5 questions, one at a time, about whatever would most change your answer. Wait for each reply. When you have enough, give the final answer and list the assumptions you made." },
            new Template { Name = "Long document: quotes first", Category = "Technique", Body = "<document>\n{{document}}\n</document>\n\nFirst, quote the parts of the document most relevant to the question below. Then answer using only those quotes, and say if the document does not contain the answer.\n\nQuestion: {{question}}" },
            new Template { Name = "Teach by examples", Category = "Technique", Body = "Do this task: {{task}}\n\nMatch the pattern in these examples.\n\n<example>\n{{example 1}}\n</example>\n<example>\n{{example 2}}\n</example>\n<example>\n{{example 3}}\n</example>\n\nInput: {{input}}" },
            new Template { Name = "Answer, attack, improve", Category = "Technique", Body = "{{request}}\n\nGive your answer. Then list the three weakest points in it, judged against this goal: {{goal}}. Then give a final, improved version." },
            new Template { Name = "Pre-mortem", Category = "Technique", Body = "My plan: {{plan}}\n\nImagine it is 6 months from now and this plan has failed badly. Write the most likely story of why. Then list the top 5 causes by likelihood, the early warning sign of each, and what I should change today." },
            new Template { Name = "Argue against me", Category = "Technique", Body = "I believe {{belief}}. Give the strongest honest case against this, as a smart person who disagrees would make it. Then say what evidence would settle it, and which side you think is right. Disagree with me if that is what the evidence supports." },
            new Template { Name = "Do the opposite", Category = "Technique", Body = "My instinct is to {{instinct}} about {{situation}}. Give me the case for doing the opposite, and when the opposite would be the better choice. Then tell me which to do in my situation, and why." },
            new Template { Name = "Compare three approaches", Category = "Technique", Body = "Goal: {{goal}}. Give three genuinely different approaches, not variations of one. For each, give the main advantage, main risk and first step. Then choose one and explain what would make you choose another." },
            new Template { Name = "Reverse engineer this content", Category = "Technique", Body = "<content>\n{{content}}\n</content>\n\nAnalyse why this content works: hook, structure, tone, length and persuasion. Then write a reusable prompt with {{variables}} that would produce new content the same way on any topic. Do not reuse its sentences." },
            new Template { Name = "Improve my prompt with me", Category = "Technique", Body = "Here is my prompt:\n\n{{prompt}}\n\nGrade it from 1 to 10 for getting the result I want, name what is missing, ask me only what you need to know, and then write the improved version." },
            new Template { Name = "List wrong assumptions first", Category = "Technique", Body = "{{question}}\n\nBefore you answer, make a list of wrong assumptions people sometimes make about the concepts in this question. Then answer. At the end, say what you left out." },
            new Template { Name = "Keep answers short (cheaper)", Category = "Technique", Body = "{{request}}\n\nAnswer in at most {{limit}}. Start with the answer. No preamble, no recap, no restating my question. Show code changes only, not whole files." },
            new Template { Name = "Reverse engineer an image", Category = "Image", Body = "Attach an image, then send this.\n\nDescribe this image as a reusable image generator prompt for {{model}}. Cover subject, setting, composition, camera angle, lens, lighting, colour palette, style and mood, in that order. Be specific enough to recreate its look on a new subject, and mark the subject as {{subject}}. Do not name real people or artists." },
            new Template { Name = "Keep a character consistent", Category = "Image", Body = "Create a reference description for this character: {{character}}. Fix face, age, build, hair, clothing and colours in exact words. Then write {{count}} image prompts placing the same character in these scenes: {{scenes}}. Repeat the exact character description in every prompt." },
            new Template { Name = "Ad poster brief", Category = "Image", Body = "Design a {{format}} ad poster for {{offer}}, for {{audience}}. Show this headline exactly, large and at the top: \"{{headline}}\". Show this smaller line at the bottom: \"{{cta}}\". Composition: {{composition}}. Use real photographic or editorial styling with a confident layout, one accent colour {{accent}}, and clean empty space around the text. Include no other text." },

            new Template { Name = "Structured report", Category = "Work", Body = "Write a {{document type}} on {{topic}} for {{audience}}: executive summary with the decision first, then findings, options with trade-offs, recommendation, owners and timeline, and risks. Separate facts from assumptions, and mark anything I must verify.\n\nSource material:\n{{material}}" },
            new Template { Name = "Short video script", Category = "Marketing", Body = "Write a {{length}} short video script on {{topic}} for {{audience}} in {{language}}, in the way people actually talk. Give a hook for the first 3 seconds, the spoken lines with timing, on-screen text, and a caption." },
            new Template { Name = "Ad variations", Category = "Marketing", Body = "Write {{count}} ad variations for {{product}}, for {{audience}}. Offer and facts: {{facts}}. Each must use a truly different angle: pain, outcome, proof, objection or curiosity. Give primary text, headline and the visual idea. Use only the facts above, and flag any claim that needs proof." },
            new Template { Name = "LinkedIn post in my voice", Category = "Writing", Body = "Write a LinkedIn post about {{topic}} in a human, reflective storytelling voice, based on this real experience: {{experience}}. Open with a specific moment, not a claim. One idea only. Short lines, no hashtags, no corporate language. End with a real question." },
            new Template { Name = "Build this feature", Category = "Coding", Body = "Project: {{project}}. I want: {{outcome}}. Who uses it: {{user}}. It is finished when: {{done}}.\n\nFirst read the existing code and tell me your plan in plain language. Keep the change as small as possible, make no destructive database changes, do not deploy without my go-ahead, and tell me exactly how to check that it works." },
        };
    }
}

class HistoryItem
{
    public string Id, When, Mode, Provider, Target, In, Out;
    public int Ms;
    public bool Fav;
}

static class History
{
    static string FilePath { get { return Path.Combine(Settings.Dir, "history.jsonl"); } }

    public static void Add(HistoryItem h)
    {
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            File.AppendAllText(FilePath, Settings.ToJson(h) + "\n", Encoding.UTF8);
        }
        catch (Exception e) { Settings.Log("history write failed: " + e.Message); }
    }

    // Read only when the History or Insights page opens, so nothing sits in memory.
    public static List<HistoryItem> Load(int keepDays)
    {
        var list = new List<HistoryItem>();
        if (!File.Exists(FilePath)) return list;
        var cutoff = DateTime.Now.AddDays(-keepDays);
        bool pruned = false;
        foreach (var line in File.ReadLines(FilePath, Encoding.UTF8))
        {
            if (line.Trim() == "") continue;
            try
            {
                var h = Settings.FromJson<HistoryItem>(line);
                DateTime when;
                if (!h.Fav && DateTime.TryParse(h.When, out when) && when < cutoff) { pruned = true; continue; }
                list.Add(h);
            }
            catch { pruned = true; }
        }
        if (pruned) Write(list);
        list.Reverse();
        return list;
    }

    public static void Write(IEnumerable<HistoryItem> items)
    {
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            File.WriteAllLines(FilePath, items.Select(h => Settings.ToJson(h)), Encoding.UTF8);
        }
        catch (Exception e) { Settings.Log("history save failed: " + e.Message); }
    }

    public static void Clear() { try { File.Delete(FilePath); } catch { } }
}
