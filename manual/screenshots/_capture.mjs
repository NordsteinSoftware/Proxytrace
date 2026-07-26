import { launch, goto, shot, waitForReady, outDir } from './capture-lib.mjs';
import { join } from 'node:path';

await waitForReady();
const { browser, page } = await launch();
const dir = outDir('traces');

await goto(page, '/traces', '[data-testid="traces-timeline"]');

// The auto-default snaps to the smallest quick range holding data — which is the sample client's
// last minute. Widen to 7 days so the strip shows the seeded history's diurnal shape.
await page.getByTestId('traces-time-trigger').click();
await page.getByTestId('traces-time-preset-7d').click();
await page.keyboard.press('Escape'); // close the picker popover before shooting
await page.waitForTimeout(2000);

await shot(page, join(dir, 'list.png'));

// The strip on its own, with the hover playhead + readout showing.
const strip = page.locator('[data-testid="traces-timeline"]');
const box = await strip.boundingBox();
await page.mouse.move(box.x + box.width * 0.63, box.y + box.height * 0.4);
await page.waitForTimeout(500);
await shot(page, join(dir, 'timeline.png'), { selector: '[data-testid="traces-timeline"]' });

await browser.close();
