import { Component, input } from '@angular/core';
import { Rejection } from '../api';

// records that failed validation in one import, with the reason
@Component({
  selector: 'app-rejections-panel',
  templateUrl: './rejections-panel.html',
  styleUrl: './rejections-panel.css',
})
export class RejectionsPanel {
  readonly batchId = input.required<number>();
  readonly rejections = input.required<Rejection[]>();
}
