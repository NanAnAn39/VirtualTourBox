# GitHub发布步骤（网页方式）

## 文件放在哪里
下载目录的 VirtualTourBox-GitHub-v0.2.5 内：
- VirtualTourBox/：准备上传到GitHub仓库的源码与README。只上传此文件夹里面的内容。
- VirtualTourBox-v0.2.5-preview-win-x64.zip：普通用户下载的完整运行包，放到Releases附件。
- VirtualTourBox-v0.2.5-preview-source.zip：源码备份，可选放到Releases；不能代替仓库内展开的源码。
- SHA256SUMS.txt：下载完整性校验，可一同放到Releases。
- 发布步骤.md：本文件。

不要把以前下载目录的整个VirtualTourBox历史开发文件夹上传。

## 第一步：创建仓库
1. 登录 https://github.com ，打开 https://github.com/new 。
2. Repository name填写 VirtualTourBox。
3. Description可填：用手柄控制Windows绘画快捷键及iPad Procreate的实验性映射工具。
4. 选Public。
5. README初始化不勾选；.gitignore与License模板均不选。许可文件已随源码准备，不能选择MIT。
6. 点Create repository。

## 第二步：上传源码
1. 在空仓库页面点 uploading an existing file。已有文件时点 Add file → Upload files。
2. 打开本地 VirtualTourBox-GitHub-v0.2.5/VirtualTourBox/。
3. 将里面的所有文件和子文件夹选中，拖入GitHub上传区。不要把最外层VirtualTourBox文件夹再套一层。
4. 确认上传列表中有README.md、LICENSE.txt、src/、third_party/等。
5. 提交说明填写：Initial v0.2.5 preview。
6. 选择提交到main，点击Commit changes；若页面先显示Propose changes，则按提示完成提交或合并。
7. 返回仓库首页，应该能直接看到中文README和界面示例图。
当前源码文件数低于100，可以一次网页上传。不要上传zip到源码区作为唯一代码内容。

## 第三步：发布下载版本
1. 仓库首页右侧找Releases，点Create a new release；已有版本时点Draft a new release。
2. Choose a tag中输入 v0.2.5-preview，选择Create new tag。
3. Target选main。
4. Release title填写 Virtual TourBox v0.2.5 预览版。
5. 描述复制源码目录 docs/RELEASE_NOTES.md 的全部内容。
6. 附件上传：
   - VirtualTourBox-v0.2.5-preview-win-x64.zip
   - SHA256SUMS.txt
   - 可选：VirtualTourBox-v0.2.5-preview-source.zip
7. 勾选 This is a pre-release。新增长按没有完成本版iPad实机验收，不应标成稳定版。
8. 等待附件全部上传完成，再点 Publish release。
GitHub自动生成的Source code(zip)只是源码；普通用户应下载我们附加的win-x64.zip。

## 第四步：发布后检查
1. 点Releases，确认能看到win-x64.zip下载附件。
2. 仓库首页README是否正常展示；标题不要写“全手柄兼容”或“全部实机验证”。
3. 把仓库或Release页面链接发给别人即可。
4. 后续改版时上传修改后的源码，再创建新版本标签与Release，不要覆盖旧版本的下载附件。

## 许可说明
项目主体是源码可查看项目，允许个人使用、查看和本地修改，禁止未经作者书面许可发布修改版/衍生版。不是MIT开源项目。
GitHub公开仓库仍允许按平台条款查看和Fork，无法通过该许可关闭Fork按钮。
third_party内windows-ble-hid等组件按其原有许可，不受项目主体限制覆盖。

官方流程参考：
- 创建仓库：https://docs.github.com/en/repositories/creating-and-managing-repositories/creating-a-new-repository
- 网页上传文件：https://docs.github.com/en/repositories/working-with-files/managing-files/adding-a-file-to-a-repository
- 管理Release：https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository
- 仓库许可：https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository
