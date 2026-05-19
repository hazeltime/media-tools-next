import { test, expect } from '@playwright/test';
import * as path from 'path';

const outDir = process.env.ARTIFACT_DIR || __dirname;

test('visual navigation and layout verification', async ({ page }) => {
  // Set viewport to a typical desktop size
  await page.setViewportSize({ width: 1280, height: 800 });

  console.log('Navigating to local Blazor Web app...');
  await page.goto('http://localhost:5069');

  // Wait for the app to render
  await expect(page.locator('.navbar-brand')).toBeVisible({ timeout: 10000 });

  // 1. Configure / Setup Page
  await page.waitForTimeout(1000); // Give fonts and HSL variables time to kick in
  await page.screenshot({ path: path.join(outDir, '01_Configure.png') });

  // 2. Run Page
  await page.click('text=Run');
  await expect(page.locator('text=Hard limits')).toBeVisible();
  await page.waitForTimeout(500);
  await page.screenshot({ path: path.join(outDir, '02_Run.png') });

  // 3. Results Page
  await page.click('text=Results · All');
  await page.waitForTimeout(500);
  await page.screenshot({ path: path.join(outDir, '03_Results.png') });

  // 4. Tools Page
  await page.click('text=Tools');
  await page.waitForTimeout(500);
  await page.screenshot({ path: path.join(outDir, '04_Tools.png') });
  
  // 5. Help Page
  await page.click('text=Help');
  await page.waitForTimeout(500);
  await page.screenshot({ path: path.join(outDir, '05_Help.png') });

  // 6. History Page
  await page.click('text=Saved scans');
  await page.waitForTimeout(500);
  await page.screenshot({ path: path.join(outDir, '06_History.png') });

  console.log('Screenshots saved to:', outDir);
});
