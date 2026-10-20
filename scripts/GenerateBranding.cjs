// Optional artwork tool: Node.js + sharp. App builds use the checked-in assets.
const fs = require('node:fs');
const path = require('node:path');
const sharp = require('sharp');
const root = path.resolve(__dirname, '..');
const output = path.join(root, 'assets', 'branding');
const source = fs.readFileSync(path.join(output, 'noteview.svg'), 'utf8');
const mark = source.replace(/  <g id="tile">[\s\S]*?<\/g>\n/, '');
const mono = mark.replace(/  <g id="staves"[\s\S]*?<\/g>\n/, '').replaceAll('url(#ink)', '#FFFFFF');
const compact = source.replace(/  <g id="staves"[\s\S]*?<\/g>\n/, '');

async function png(svg, size) {
  return sharp(Buffer.from(svg), { density: 384 }).resize(size, size).png().toBuffer();
}
// Small ICO entries use classic 32-bit DIBs for WinForms/GDI compatibility.
async function dib(svg, size) {
  const pixels = await sharp(await png(svg, size)).ensureAlpha().raw().toBuffer();
  const maskStride = Math.ceil(size / 32) * 4;
  const result = Buffer.alloc(40 + size * size * 4 + maskStride * size);
  result.writeUInt32LE(40, 0); result.writeInt32LE(size, 4); result.writeInt32LE(size * 2, 8);
  result.writeUInt16LE(1, 12); result.writeUInt16LE(32, 14);
  result.writeUInt32LE(size * size * 4 + maskStride * size, 20);
  for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) {
    const from = (y * size + x) * 4;
    const to = 40 + ((size - y - 1) * size + x) * 4;
    result[to] = pixels[from + 2]; result[to + 1] = pixels[from + 1];
    result[to + 2] = pixels[from]; result[to + 3] = pixels[from + 3];
    if (pixels[from + 3] === 0)
      result[40 + size * size * 4 + (size - y - 1) * maskStride + (x >> 3)] |= 0x80 >> (x & 7);
  }
  return result;
}
async function main() {
  fs.writeFileSync(path.join(output, 'noteview-mark.svg'), mark);
  fs.writeFileSync(path.join(output, 'noteview-mono.svg'), mono);
  fs.writeFileSync(path.join(output, 'noteview-mono-dark.svg'), mono.replaceAll('#FFFFFF', '#142630'));
  for (const size of [256, 1024]) fs.writeFileSync(path.join(output, `noteview-${size}.png`), await png(source, size));
  fs.writeFileSync(path.join(output, 'noteview-mark.png'), await png(mark, 1024));
  const sizes = [16, 20, 24, 32, 48, 64, 128, 256];
  const images = [];
  for (const size of sizes) images.push(size <= 64 ? await dib(size <= 24 ? compact : source, size) : await png(source, size));
  const directory = Buffer.alloc(6 + sizes.length * 16);
  directory.writeUInt16LE(1, 2); directory.writeUInt16LE(sizes.length, 4);
  let offset = directory.length;
  for (let i = 0; i < sizes.length; i++) {
    const at = 6 + i * 16;
    directory[at] = directory[at + 1] = sizes[i] === 256 ? 0 : sizes[i];
    directory.writeUInt16LE(1, at + 4); directory.writeUInt16LE(32, at + 6);
    directory.writeUInt32LE(images[i].length, at + 8); directory.writeUInt32LE(offset, at + 12);
    offset += images[i].length;
  }
  fs.writeFileSync(path.join(output, 'noteview.ico'), Buffer.concat([directory, ...images]));

  const embed = data => `data:image/png;base64,${data.toString('base64')}`;
  const icon = embed(await png(source, 512));
  const transparent = embed(await png(mark, 256));
  const white = embed(await png(mono, 128));
  const small = await Promise.all([16, 24, 32, 48].map(s => png(s <= 24 ? compact : source, s)));
  const board = `<svg xmlns="http://www.w3.org/2000/svg" width="1440" height="900" viewBox="0 0 1440 900">
    <rect width="1440" height="900" fill="#F3F2EF"/>
    <text x="72" y="83" font-family="Segoe UI,sans-serif" font-size="15" font-weight="600" letter-spacing="3" fill="#59666E">NOTEVIEW / VISUAL IDENTITY</text>
    <rect x="64" y="122" width="1312" height="470" rx="32" fill="#0C1922"/>
    <image href="${icon}" x="125" y="185" width="340" height="340"/>
    <text x="550" y="340" font-family="Segoe UI,sans-serif" font-size="94" font-weight="600" letter-spacing="-3" fill="#F0F1EE">NoteView</text>
    <text x="558" y="395" font-family="Segoe UI,sans-serif" font-size="22" letter-spacing="3" fill="#8EA5AE">SEE THE HARMONY.</text>
    <rect x="560" y="446" width="42" height="5" rx="2.5" fill="#F4C783"/>
    <rect x="614" y="446" width="42" height="5" rx="2.5" fill="#DCBDE0"/>
    <rect x="668" y="446" width="42" height="5" rx="2.5" fill="#78E5D5"/>
    <rect x="64" y="622" width="420" height="210" rx="24" fill="#E7E9E6"/>
    <image href="${transparent}" x="207" y="651" width="144" height="144"/>
    <text x="89" y="659" font-family="Segoe UI,sans-serif" font-size="13" letter-spacing="1.5" fill="#59666E">TRANSPARENT</text>
    <rect x="510" y="622" width="420" height="210" rx="24" fill="#142630"/>
    <image href="${white}" x="656" y="659" width="128" height="128"/>
    <text x="535" y="659" font-family="Segoe UI,sans-serif" font-size="13" letter-spacing="1.5" fill="#829DA8">MONOCHROME</text>
    <rect x="956" y="622" width="420" height="210" rx="24" fill="#FFFFFF"/>
    <text x="981" y="659" font-family="Segoe UI,sans-serif" font-size="13" letter-spacing="1.5" fill="#59666E">SMALL, STILL DISTINCT</text>
    ${small.map((data, i) => `<image href="${embed(data)}" x="${1000 + i * 88}" y="${744 - [16,24,32,48][i]/2}" width="${[16,24,32,48][i]}" height="${[16,24,32,48][i]}"/>`).join('')}
    <text x="72" y="872" font-family="Segoe UI,sans-serif" font-size="13" fill="#7D8588">Two notes. One N. A spectrum of harmony.</text>
  </svg>`;
  const previewPath = path.join(root, 'artifacts', 'branding');
  fs.mkdirSync(previewPath, { recursive: true });
  fs.writeFileSync(path.join(previewPath, 'noteview-brand-board.svg'), board);
  await sharp(Buffer.from(board)).png().toFile(path.join(previewPath, 'noteview-brand-board.png'));
  console.log('Generated vector, transparent, monochrome, PNG, ICO and preview assets.');
}
main().catch(error => { console.error(error); process.exitCode = 1; });
