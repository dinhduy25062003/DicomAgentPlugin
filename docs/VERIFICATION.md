# Báo cáo triển khai và kiểm chứng

## Cập nhật Windows 0.1.1 — 10/11/2026

- Publish Windows self-contained cho Host, Desktop và SCU thành công. Setup đóng kèm SCU; script cài vào `%SystemDrive%\DicomSimulator`, giữ nguyên dữ liệu/configuration hiện hữu.
- Desktop dùng Windows GUI subsystem (kiểm tra PE: subsystem = 2), không tạo console khi khởi chạy ứng dụng.
- Thêm icon và menu tray Windows: Open khôi phục cửa sổ; X ẩn cửa sổ; Exit desktop thoát UI, không dừng service. Đường đóng do OS/app shutdown được phép đóng bình thường.
- Build Release: 0 warnings, 0 errors. Regression tests Linux: 24 unit + 19 integration PASS. Headless test kiểm tra tài nguyên icon, đóng ẩn không hủy cửa sổ, khôi phục cửa sổ từ trạng thái minimized và đóng thật khi bỏ chế độ tray.
- PowerShell parser và Bash syntax PASS. Cài/nâng cấp/ACL và thao tác tray thực tế trên Windows: NOT RUN trong phiên phát triển này; cần kiểm tra trên Windows VM. Vị trí icon trong nhóm hidden icons do Windows quản lý.
- Setup 0.1.1 đã build: `publish/installers/DicomAgent-0.1.1-win-x64-Setup.exe` (57.281.419 bytes). Kiểm tra archive và extract bằng 7-Zip PASS; SHA-256 của 417 file payload Host/Desktop/SCU khớp publish. SHA-256 installer: `fd91ee903eab787fe969089beb9249fa45a0f65c72d88a30b60d703ea3bf34b9`, được thêm vào `publish/installers/SHA256SUMS`.

Kiểm tra trên Windows sau khi cài bản mới: SCU tồn tại; mở Start Menu không có terminal; X làm mất cửa sổ khỏi taskbar nhưng icon tray còn; Open mở lại; Exit desktop kết thúc UI; C-ECHO/C-STORE vẫn thành công nhờ service; ảnh cũ/settings vẫn giữ sau nâng cấp. Thoát UI qua menu tray trước nâng cấp tiếp theo.

## Báo cáo gốc 0.1.0

Ngày: 09/11/2026, môi trường Linux Ubuntu 26.04 x64; SDK 8.0.422 / runtime 8.0.28. Source là .NET 8/C# 12. Repository ban đầu trống, không có source hay dữ liệu khách hàng để thay đổi.

## Projects và kiến trúc

Tạo tám project trong `DicomAgent.sln`:

| Project | Trách nhiệm |
|---|---|
| DicomAgent.Domain | Entity metadata, không dependency framework |
| DicomAgent.Application | DTO/contracts, validation, path policy, IPC client |
| DicomAgent.Infrastructure | fo-dicom SCP, file store, EF/SQLite, migration/reconciliation, IPC server |
| DicomAgent.Host | Worker, DI, config, Windows Service/systemd lifecycle |
| DicomAgent.Desktop | Avalonia MVVM, dashboard, settings, history, browse/copy/open location |
| DicomAgent.UnitTests | Validation/config/UID/path/hash protocol bounds |
| DicomAgent.IntegrationTests | DICOM networking, storage/DB failure, IPC, Host process, headless UI |
| DicomAgent.Scu | CLI C-ECHO/C-STORE và tạo DX fixture tổng hợp |

Domain ← Application ← Infrastructure; Host tham chiếu Application/Infrastructure; Desktop chỉ tham chiếu Application. Test project có thể tham chiếu nhiều adapter để kiểm chứng tích hợp. Không upload website, không cloud dependency runtime.

## Kết quả

| Kiểm chứng | Kết quả | Bằng chứng/phạm vi |
|---|---|---|
| Restore package .NET 8 | PASS | Package/API thực tế; fo-dicom 5.2.6, EF Core 8.0.22, Avalonia 11.3.7 |
| Release solution build | PASS | 0 warnings, 0 errors |
| Unit tests | PASS | 24/24 |
| Integration tests trên Linux | PASS | 19/19, tổng cộng 43 test pass, 0 fail, 0 skipped trong lượt Linux |
| C-ECHO và C-STORE | PASS | SCU/SCP fo-dicom qua TCP loopback thật |
| Implicit/Explicit VR Little Endian | PASS | Transfer Syntax đọc lại đúng, Pixel Data 2×2 và PatientName tổng hợp được bảo toàn |
| Metadata/history/hash | PASS | SQLite migration, một record/file, SHA-256 và size khớp |
| Duplicate/cùng UID khác nội dung | PASS | Không overwrite, không thêm record; retransmit từ Calling AE khác cũng nhận dạng duplicate |
| Concurrent C-STORE | PASS | 6 request đồng thời, gồm UID trùng và UID khác |
| Missing UID/optional tags | PASS | Thiếu required bị từ chối, optional null được xử lý |
| Called AE / Calling AE / IP allowlist | PASS | Association không được phép bị từ chối |
| Port bị chiếm | PASS | Fail fast, không vòng lặp restart CPU spike |
| File write failure/size limit | PASS | Trả failure hoặc abort, không công bố file hoàn chỉnh/history |
| SQLite failure | PASS | Test repository lỗi và table SQLite bị drop: file giữ lại; mô phỏng phục hồi repository + reconcile hai lượt không duplicate |
| IPC quản trị | PASS | Status/settings/save/history, reject operation không hợp lệ, reject cấu hình sai, Unix socket không có quyền other |
| Đóng kết nối quản trị | PASS | C-ECHO/C-STORE tiếp tục, Host độc lập client |
| Host restart và persistent config | PASS | Chạy tiến trình Host thật, lưu AE mới, SIGTERM exit 0, restart dùng AE mới và history cũ |
| Avalonia headless | PASS | XAML khởi tạo, 3 tab, status binding cập nhật; không tương đương kiểm thử tương tác trên desktop thật |
| Publish linux-x64 self-contained | PASS | Host single-file/native SQLite, Desktop multi-file/native dependencies |
| Artifact Linux self-contained chạy thực tế | PASS | DOTNET_ROOT trỏ vị trí không có runtime; C-ECHO 0000, C-STORE 0000 hai lượt, một file/row, SHA-256/IPC đúng, SIGTERM exit 0/socket được xóa |
| Publish win-x64 self-contained | PASS | Cross-publish từ Linux, xác nhận cả hai executable PE32+ x64 |
| Native libSkiaSharp trên Linux hiện tại | PASS | `ldd` resolve các dependency; không chứng minh mọi distro target đều có đủ native libraries |
| Bash/PowerShell syntax | PASS | `bash -n` và PowerShell AST parser; chưa cài service thật |
| NuGet advisory | PASS tại thời điểm kiểm tra | `dotnet list ... package --vulnerable --include-transitive` không còn advisory ở cả 8 project sau pin SQLitePCLRaw 2.1.14 và Tmds.DBus.Protocol 0.21.3 |
| Windows runtime/service/ACL/firewall | NOT RUN | Không có máy Windows trong phiên này |
| Linux cài systemd, reboot và firewall thật | NOT RUN | Không thay đổi OS services/firewall của máy development |
| Desktop thao tác thủ công trên Windows/Linux | NOT RUN | Chỉ build/publish + headless XAML/binding |
| Máy X-quang thật / máy LAN khác | NOT RUN | Networking tests dùng loopback và dữ liệu tổng hợp |
| DICOM viewer độc lập | NOT RUN | File chỉ được đọc lại/so Pixel Data bằng fo-dicom |
| Installer EXE và .deb | BUILT | NSIS Setup.exe và deb amd64 0.1.0; chi tiết tại PACKAGES.md. Chưa ký số/chưa test cài/gỡ trên OS mục tiêu; MSI chưa tạo |
| GitHub Actions matrix | Chỉ tạo workflow | Chưa push/chạy CI trên GitHub |

Báo cáo TRX tại `tests/DicomAgent.UnitTests/TestResults/verification.trx` và `tests/DicomAgent.IntegrationTests/TestResults/verification.trx` (build artifacts bị gitignore).

Hai advisory phát hiện lúc kiểm tra dependency ban đầu đã được xử lý bằng dependency pin: [D-Bus advisory](https://github.com/advisories/GHSA-xrw6-gwf8-vvr9), [SQLite advisory](https://github.com/advisories/GHSA-2m69-gcr7-jv3q). Không có advisory hiện tại không đồng nghĩa đã security-audit đầy đủ.

## Lệnh tái hiện

Từ root repository:

```bash
dotnet restore DicomAgent.sln
dotnet build DicomAgent.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test DicomAgent.sln -c Release --no-build --logger trx
bash deploy/linux/publish.sh
python3 tools/smoke-published-linux.py
pwsh -NoProfile -File deploy/windows/publish.ps1
dotnet list DicomAgent.sln package --vulnerable --include-transitive
```

Artifacts local: `publish/linux/{host,desktop}` và `publish/windows/{host,desktop}`. Source/publish scripts có thể tạo lại, không cần commit binaries.

## Chức năng hoàn thành

Receiver C-ECHO/C-STORE; negotiation SOP/Transfer Syntax explicit; validation AE/IP/UID/size; atomic final filename; SHA-256/canonical duplicate comparison; chống path traversal/symlink; metadata nullable; SQLite migration/unique index; giữ file khi DB lỗi và reconciliation; Worker service lifecycle; persistent settings; IPC quyền OS; Avalonia dashboard/settings/history; SCU/generator; deployment scripts; README/packaging/troubleshooting và CI matrix.

## Các giới hạn còn lại

- Cần kiểm thử hiện trường, Windows service/IPC ACL, Linux systemd/reboot, UI native/clipboard/folder picker, DICOM viewer độc lập và modality thật trước sử dụng lâm sàng.
- Chỉ hai Transfer Syntax uncompressed Little Endian và danh sách Storage SOP trong `StorageScp.cs`; chưa TLS, compressed codecs, Storage Commitment, Query/Retrieve, DICOMweb.
- Serialize Part 10 bảo toàn dataset/Pixel Data theo test; không byte-for-byte identity của file đầu vào.
- Mỗi storage root phải do một Host sở hữu. Commit/reconcile được serialize; dò duplicate và enumerate reconciliation còn phụ thuộc số file, cần catalog tối ưu khi dữ liệu rất lớn.
- Reconciliation chỉ quét root hiện hành, bỏ qua file đã index; không kiểm tra định kỳ file bị thay đổi/xóa ngoài Agent. Một file hỏng chưa index có thể làm vòng reconciliation dừng và báo lỗi; cần quản trị kiểm tra/phục hồi file rồi chạy tiếp.
- Lỗi schema/DB vật lý cần khôi phục SQLite; migration không tự sửa table bị xóa bằng tay. Các file hoàn chỉnh không bị mất vì lỗi này.
- Cấu hình áp dụng sau restart service; không auto restart qua UI. Environment/CLI override vẫn ưu tiên hơn settings lưu.
- Dashboard recent errors chỉ trong RAM phiên hiện tại; history UI giới hạn 100 record. Khi history tắt hoặc DB không truy cập được, dùng thống kê file thay thế nếu query DB thất bại.
- Shutdown chờ tối đa khoảng 15 giây cho associations; vượt hạn có thể ngắt truyền và thiết bị cần retry. `.partial` sau crash giữ để phục hồi thủ công. Không cam kết chống mất điện/hỏng ổ đĩa.
- DIMSE request timeout 30 giây không phải idle timeout tuyệt đối cho mọi association; đặt Receiver trên mạng được kiểm soát, giới hạn thiết bị qua firewall.
- Không tự xóa ảnh, không mã hóa ổ đĩa/SQLite sẵn; BitLocker/LUKS và backup/retention là cấu hình vận hành của phòng khám.
- Đã bổ sung NSIS Setup.exe và deb amd64; chưa tạo MSI, chưa ký số, chưa kiểm thử cài/gỡ package trên OS mục tiêu. Xem PACKAGES.md.

Danh sách file source/config/deploy/test đầy đủ: [FILES.md](FILES.md). Hướng dẫn chạy, cấu hình thiết bị và test từ máy khác: [README](../README.md).
