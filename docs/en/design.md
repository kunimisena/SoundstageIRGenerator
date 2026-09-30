# Design philosophy and signal model

[简体中文](../zh-CN/design.md) · [Home](../../README.en.md)

## Listening and tuning come first

Soundstage IR Generator adds space to headphone music through an editable sound field. Energy spectra, decay times and directional distributions correspond to audible changes. Saved parameters and random seeds make tuning reproducible and shareable.

A set of equivalent arrival directions around the head describes the reflected field. Each direction has a statistical kernel, rendered through head shadow, interaural timing, pinnae and torso responses. Six to twelve directions provide a practical editing space: a few distinct spectral and temporal profiles offer considerable variety.

**Research constrains the statistics; listening guides the settings.** Broad spectra and decay curves set the overall color and tail, while causal random kernels supply fine temporal and spectral detail. The authored presets offer starting points from compact monitoring to enveloping, long-tailed fields.

The field is authored for headphone listening: diffuse temporal detail and directional head cues can be tuned without prescribing a wall layout or particular loudspeaker. Broad tonal correction is part of that design. Minimum-phase EQ adjusts the smoothed spectral balance while preserving the statistical tail as a full impulse response; its strength remains a listening choice.

## Direction kernels become four paths

For each direction d, fixed kernels `g[d,L]` and `g[d,R]` describe responses to the two inputs. The same direction/input kernel feeds both corresponding ear filters, `h[left,d]` and `h[right,d]`.

\[
H_{e\leftarrow i}=H^{direct}_{e\leftarrow i}+\sum_d h_{e,d}*g_{d,i}
\]

Here i identifies the input, e identifies the ear, and `*` denotes convolution. The outputs are:

\[
y_l=H_{l\leftarrow L}*x_L+H_{l\leftarrow R}*x_R,\qquad
y_r=H_{r\leftarrow L}*x_L+H_{r\leftarrow R}*x_R.
\]

Direct speakers also pass through the head, optionally preceded by distance-dependent air absorption. The result is a fixed 2×2 linear system. Common normalization preserves relative path amplitudes and timing.

## Energy, decay and phase are generated together

The generator uses smooth, overlapping bands spaced approximately one third of an octave apart, with power-complementary weights. Gaussian noise and a causal envelope create each band's response. The energy curve controls the integrated broad spectrum, the decay curve controls frequency-dependent time scales, and the full envelope controls local mean energy through onset, buildup and the tail.

Changing decay time recalibrates band energy, allowing tail length and total wet energy to be adjusted separately. Reflection density follows the buildup scale; energy during buildup may rise, remain level or fall.

Phase emerges from the complete time signal alongside magnitude and decay. In the simplified model `g(t)=a(t)w(t)`, with white noise w:

\[
\mathbb E[G(f)G^*(f+\Delta f)]=\int |a(t)|^2e^{j2\pi\Delta f t}\,dt.
\]

The relation connects temporal energy distribution with cross-frequency correlation. Longer tails create finer spectral variation. Independent kernels with the same statistics add their covariances; energy normalization preserves their normalized correlation structure. Directions with different spectra or envelopes contribute according to their own profiles and weights.

The seed, stable source ID, input stream and fixed band order determine each realization. Editing one source retains other sources' random detail, supporting controlled comparisons.

## Head models and the common response

The sphere offers a compact head-shadow and arrival-time model. FABIAN supplies head, pinna and torso responses; the renderer samples the nearest direction from the fixed HATO 0 dataset. Complex averaging of mirrored ear responses makes anatomical symmetry match the parameter layout.

FABIAN defaults to the dataset author's smooth minimum-phase inverse common transfer function. Removing that shared spectrum lets directional differences combine with the listener's existing headphone calibration. Disabling it provides the complete dataset spectrum for comparison. Headphone calibration remains in the listener's playback chain.

The data represents blocked ear-canal entrances, with author-provided numerical completion at some low frequencies and lower directions. Sources, transformations and attribution are documented in the [FABIAN notices](../../THIRD_PARTY_NOTICES.md).

## EQ is an adjustable tonal choice

For identical input signals, each ear receives:

\[
M_l=H_{l\leftarrow L}+H_{l\leftarrow R},\quad M_r=H_{r\leftarrow L}+H_{r\leftarrow R}.
\]

With independent mirrored random detail, stage one equalizes the smoothed magnitude of each ear response. Stage two designs a common EQ from `(Q_l M_l + Q_r M_r)/2`. Strict random mirroring uses one common first-stage EQ. Filters are minimum phase; strength scales the correction in dB before filter construction.

EQ gain follows the smoothed target. Actual minimum-phase FIR response error determines whether its support needs extending. Local positive-power integration avoids cancellation between loud and quiet spectral regions; zero or nonfinite references produce an explicit error. A common scalar calibrates the selected output reference level. Frequency peaks are reported as playback-headroom information.

Defaults are 100% for stage one, 0% for stage two, and smoothing of 1/24 and 1/3 octave respectively. Common correction designed from identical inputs also changes lateral content. Adjustable strength lets the listener choose the balance for their music.

A shared 20 Hz–20 kHz minimum-phase bandpass and a common gain calibration follow EQ. Analysis uses the generated kernels, exposing fine detail, smoothed spectra, impulses and decay so that listening observations can be compared with actual output.

## From FABIAN to the sound at your ears

The generator starts from an already tuned headphone playback system. FABIAN supplies directional head cues, statistical reverberation supplies an editable space, and the physical headphones turn the two electrical signals into sound at the ears. Existing headphone EQ continues to provide the listener's familiar tonal reference.

The complete path from digital input to the eardrums is:

```text
Stereo music → four-path convolution matrix K → existing headphone EQ → headphones and acoustic coupling → ears
```

In the frequency domain, neglecting acoustic leakage between the two headphone channels, the left-ear relationship is:

```text
p_left(f) = P_left(f) × [K_left←L(f) × x_L(f) + K_left←R(f) × x_R(f)]
```

The right ear follows the same form. K includes the generator's head rendering, reverberation, final EQ and gain. P combines the user's headphone EQ, the headphones' electroacoustic response and their coupling to the pinna and ear canal when worn. This separates the digital sound field from the physical playback path.

### What each correction does

| Processing | What it addresses |
|---|---|
| FABIAN common-response compensation | Uses the dataset's smoothed inverse common transfer function (CTF) to compensate shared spectral structure while retaining directional differences |
| Generator minimum-phase EQ | Shapes the smoothed digital output spectrum using the current four-path matrix and the selected reference input |
| Existing headphone EQ | Brings the physical headphones toward the listener's chosen tonal target, providing the playback foundation |

The plots describe the final digital kernels. Eardrum pressure also depends on P; predicting that path precisely requires the headphone transfer function for the listener wearing those headphones. Matching a preferred headphone target provides a practical playback reference, whereas measuring and inverting an individual headphone transfer function serves a different calibration goal. FABIAN's inverse CTF concerns the head dataset's common response rather than the inverse response of a particular headphone.

### How spatial cues carry through playback

When the two playback transfer functions are approximately equal, `P_left(f) ≈ P_right(f)`, they mainly act as a common filter. For a given virtual direction, that common factor cancels in the complex response ratio between the ears. The digital interaural level and relative phase differences can therefore be approximately preserved.

Common filtering still changes the relative weighting of frequency bands, and monaural pinna spectral cues are also affected by playback. Differences in left/right coupling, headphone placement and the listener's anatomy relative to FABIAN all contribute to the resulting timbre, direction and externalization. The project therefore uses existing headphone tuning as a reference and leaves direction distribution, reverberation and spatial EQ available for listening-based adjustment.

Headphone transfer functions and placement variability are also important in binaural reproduction research. [Schärer and Lindau (2009)](https://www2.users.ak.tu-berlin.de/akgroup/ak_pub/2009/Schaerer_2009_Evaluation_of_Equalization_Methods_for_Binaural_Signals.pdf) compare headphone equalization methods and discuss individual differences and repositioning effects.

This approach serves the goal of adding adjustable space to a headphone tonal balance the listener already enjoys. Reproducing the sound pressure of real speakers more precisely can involve measuring individual headphone transfer functions and designing playback compensation at a consistent measurement reference position. FABIAN uses blocked ear-canal entrances as its reference; that extension also needs to account for entrance-to-eardrum transmission and the headphones' acoustic loading. The project's general-purpose approach combines existing headphone tuning, common-response compensation and adjustable digital tonal correction.

## Preset authorship

Wide monitor embeds the author-approved configuration, including source identities, curve shapes, 8% reflected energy and 1/12-octave first-stage EQ smoothing. General defaults remain 1/24 octave.

Control room is an authored statistical field informed by control-room listening comparisons: direct sound dominates, an early energy drop leads into a quieter short tail, and upper-frequency reflected energy and decay gradually decrease. The 0.22 s tail scale and 5% wet energy are design choices. The eight mirrored directions have modest differences in level and timing. The full envelope uses points −2, −6, −20, −36, −60 dB at normalized positions −1, 0, 0.08, 0.35, 1. This separates a quick early decline from a small residual tail. Measured output is checked separately from these reference scales.

Free field removes the statistical reflection contribution and retains the two head-filtered speakers. Common-response removal, EQ and output band limiting remain available as in the other templates.

## Research background

[Traer and McDermott (2016)](https://mcdermottlab.mit.edu/papers/Traer_McDermott_2016_reverberation.pdf) study the statistics and perception of natural reverberation, informing frequency-dependent decay. [The FABIAN dataset and paper](https://doi.org/10.14279/depositonce-5718.4) provide directional head responses. Statistical synthesis, authored presets and adjustable EQ form this project's field-design method.
