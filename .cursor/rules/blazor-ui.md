# Blazor UI & Styling Rules
GlobPattern: **/*.razor | **/*.css

You are an agentic developer working on the desktop Blazor UI or styling. Follow these dynamic rules:

## 1. Web Stack
- Core Technologies: HTML, Razor, C#, and Vanilla CSS.
- Styling: Prioritize Vanilla CSS for flexibility and control. Avoid adding TailwindCSS or other bloated styling dependencies unless explicitly requested.

## 2. Design Aesthetics
- Keep layouts clean, professional, responsive, and modern.
- Maintain consistent margins, paddings, and font sizes using standard CSS custom properties (variables) defined in main styles.
- Support smooth hover effects and interactive states.

## 3. Token-Efficient UI Edits
- When modifying `.razor` files, only edit the relevant HTML tags or `@code` blocks.
- Minimize file reads on heavy CSS/Razor components. Use focused `grep_search` to find elements or CSS classes.
- Use `scripts\verify-fast.ps1 -Area desktop` to build the MAUI Blazor desktop shell and verify compilation.
