export function Description({ text }: { text: string }) {
  return (
    <pre className="mt-4 whitespace-pre-wrap break-words rounded-[10px] border border-border bg-panel2 p-3.5 font-sans text-[12.5px] leading-relaxed text-[#c7cedd]">
      {text}
    </pre>
  );
}
